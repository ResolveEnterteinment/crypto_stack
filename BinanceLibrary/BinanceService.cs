using Application.Interfaces.Exchange;
using Application.Interfaces.Logging;
using Binance.Net;
using Binance.Net.Clients;
using Binance.Net.Objects.Models.Spot;
using CryptoExchange.Net.Authentication;
using CryptoExchange.Net.CommonObjects;
using CryptoExchange.Net.Objects;
using Domain.Constants;
using Domain.DTOs;
using Domain.DTOs.Exchange;
using Domain.Exceptions;
using MongoDB.Driver.Linq;
using Polly;
using Polly.Retry;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace BinanceLibrary
{
    /// <summary>
    /// Service for interacting with the Binance exchange API.
    /// </summary>
    public class BinanceService : IExchange
    {
        private readonly string _reserveAssetTicker;
        private readonly BinanceRestClient _binanceClient;
        private readonly ILoggingService _logger;
        private readonly AsyncRetryPolicy _retryPolicy;

        // Dictionary to map Binance OrderStatus to our application OrderStatus
        private static readonly Dictionary<Binance.Net.Enums.OrderStatus, string> StatusMap = new()
        {
            { Binance.Net.Enums.OrderStatus.New, OrderStatus.Pending },
            { Binance.Net.Enums.OrderStatus.PendingNew, OrderStatus.Pending },
            { Binance.Net.Enums.OrderStatus.Filled, OrderStatus.Filled },
            { Binance.Net.Enums.OrderStatus.PartiallyFilled, OrderStatus.PartiallyFilled },
            { Binance.Net.Enums.OrderStatus.Canceled, OrderStatus.Failed },
            { Binance.Net.Enums.OrderStatus.Rejected, OrderStatus.Failed },
            { Binance.Net.Enums.OrderStatus.Expired, OrderStatus.Failed }
        };

        public string Name => "BINANCE";

        public string QuoteAssetTicker
        {
            get => _reserveAssetTicker;
            set => throw new NotSupportedException("ReserveAssetTicker can only be set during initialization");
        }

        /// <summary>
        /// Maps Binance order status to application order status
        /// </summary>
        private string? MapBinanceStatus(Binance.Net.Enums.OrderStatus status) =>
            StatusMap.TryGetValue(status, out var mappedStatus) ? mappedStatus : null;

        /// <summary>
        /// Initializes a new instance of the <see cref="BinanceService"/> class.
        /// </summary>
        /// <param name="binanceSettings">Binance API configuration settings</param>
        /// <param name="logger">Logger for recording diagnostic information</param>
        public BinanceService(ExchangeSettings binanceSettings, ILoggingService logger)
        {
            // Input validation
            ArgumentNullException.ThrowIfNull(binanceSettings, nameof(binanceSettings));
            ArgumentNullException.ThrowIfNull(logger, nameof(logger));

            if (string.IsNullOrWhiteSpace(binanceSettings.ApiKey))
                throw new ArgumentException("Binance API Key cannot be null or empty", nameof(binanceSettings));

            if (string.IsNullOrWhiteSpace(binanceSettings.ApiSecret))
                throw new ArgumentException("Binance API Secret cannot be null or empty", nameof(binanceSettings));

            if (string.IsNullOrWhiteSpace(binanceSettings.ReserveStableAssetTicker))
                throw new ArgumentException("Reserve stable asset ticker cannot be null or empty", nameof(binanceSettings));

            _logger = logger;
            _reserveAssetTicker = binanceSettings.ReserveStableAssetTicker;

            // Configure retry policy for transient errors
            _retryPolicy = Policy
                .Handle<Exception>(ex => IsTransientException(ex))
                .WaitAndRetryAsync(
                    3, // Number of retry attempts
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), // Exponential backoff
                    (ex, timeSpan, retryCount, context) =>
                    {
                        _logger.LogWarning("Binance API call failed. Retrying {RetryCount}/3 after {RetryInterval}ms. Error: {ErrorMessage}",
                            retryCount, timeSpan.TotalMilliseconds, ex.Message);
                    }
                );

            // Initialize Binance client with secure credential handling
            _binanceClient = new BinanceRestClient(options =>
            {
                options.ApiCredentials = new ApiCredentials(binanceSettings.ApiKey, binanceSettings.ApiSecret);
                options.Environment = binanceSettings.IsTestnet ? BinanceEnvironment.Testnet : BinanceEnvironment.Live;

                // Set reasonable timeouts for API requests
                options.RequestTimeout = TimeSpan.FromSeconds(30);

                // Note: Logging configuration is handled via the general logger (_logger) 
                // passed into this class, rather than through client options
            });

            _logger.LogInformation("Initialized Binance service with environment: {Environment}, ReserveAsset: {ReserveAsset}",
                binanceSettings.IsTestnet ? "Testnet" : "Production", _reserveAssetTicker);
        }

        /// <summary>
        /// Determines if an exception is transient and can be retried
        /// </summary>
        private bool IsTransientException(Exception ex)
        {
            // Network-related exceptions that might be temporary
            return ex is HttpRequestException ||
                   ex is TimeoutException ||
                   ex is System.Net.WebException ||
                   // Binance rate limit or server errors
                   (ex.Message?.Contains("429") ?? false) || // Rate limit
                   (ex.Message?.Contains("5") ?? false);    // 5xx server errors
        }

        /// <summary>
        /// Places an order on the Binance exchange
        /// </summary>
        internal async Task<BinancePlacedOrder> PlaceOrder(
            string symbol,
            decimal quantity,
            string paymentProviderId,
            Binance.Net.Enums.OrderSide side = Binance.Net.Enums.OrderSide.Buy,
            Binance.Net.Enums.SpotOrderType type = Binance.Net.Enums.SpotOrderType.Market)
        {
            using var activity = new Activity("BinanceService.PlaceOrder").Start();
            activity.SetTag("symbol", symbol);
            activity.SetTag("quantity", quantity);
            activity.SetTag("side", side.ToString());
            activity.SetTag("orderType", type.ToString());
            activity.SetTag("paymentProviderId", paymentProviderId);

            try
            {
                _logger.LogInformation(
                    "Placing Binance order: Symbol={Symbol}, Side={Side}, Type={Type}, Quantity={Quantity}, PaymentId={PaymentId}",
                    symbol, side, type, quantity, paymentProviderId);

                // Add idempotency by using payment provider ID as client order ID
                return await _retryPolicy.ExecuteAsync(async () =>
                {
                    var orderResult = await _binanceClient.SpotApi.Trading.PlaceOrderAsync(
                        symbol,
                        side,
                        type,
                        quantity: side == Binance.Net.Enums.OrderSide.Sell ? quantity : null,
                        quoteQuantity: side == Binance.Net.Enums.OrderSide.Buy ? quantity : null,
                        newClientOrderId: FormatClientOrderId(paymentProviderId))
                    .ConfigureAwait(false);

                    if (!orderResult.Success)
                    {
                        _logger.LogError("Binance order placement failed: {ErrorCode} - {ErrorMessage}",
                            orderResult.Error?.Code, orderResult.Error?.Message);
                        throw new ExchangeApiException(
                            $"Error placing Binance order: Code {orderResult.Error?.Code}, Message: {orderResult.Error?.Message}",
                            Name);
                    }

                    _logger.LogInformation("Successfully placed Binance order: OrderId={OrderId}, Status={Status}",
                        orderResult.Data.Id, orderResult.Data.Status);

                    return orderResult.Data;
                });
            }
            catch (Exception ex) when (ex is not ExchangeApiException)
            {
                // Wrap non-application exceptions
                var message = $"Error placing Binance order: {ex.Message}";
                _logger.LogError(message);
                throw new ExchangeApiException(message, Name, ex);
            }
        }

        /// <summary>
        /// Formats client order ID to ensure it meets Binance requirements
        /// </summary>
        private string FormatClientOrderId(string paymentProviderId)
        {
            // Binance requires client order IDs to be alphanumeric and max 36 chars
            // We'll use a prefix + hash approach to keep lengths consistent
            const string prefix = "CS_";

            // Use last 32 chars max to fit within Binance limits
            if (paymentProviderId.Length <= 33)
                return prefix + paymentProviderId;

            // For longer IDs, hash them for consistency
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(paymentProviderId));
            var hashString = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

            return prefix + hashString;
        }

        /// <summary>
        /// Places a spot market buy order using the provided API credentials.
        /// For spot market orders on Binance, the amount to be spent is provided via the quoteOrderQuantity parameter.
        /// </summary>
        /// <param name="symbol">The trading pair (e.g., BTCUSDT).</param>
        /// <param name="quantity">The amount of the quote asset (e.g., USDT) to spend.</param>
        /// <param name="paymentProviderId">Unique ID for tracking this payment</param>
        /// <returns>The order details.</returns>
        public async Task<PlacedExchangeOrder> PlaceSpotMarketBuyOrder(string symbol, decimal quantity, string paymentProviderId)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                throw new ArgumentException("Symbol cannot be null or empty", nameof(symbol));

            if (quantity <= 0)
                throw new ArgumentException("Quantity must be greater than zero", nameof(quantity));

            if (string.IsNullOrWhiteSpace(paymentProviderId))
                throw new ArgumentException("Payment provider ID cannot be null or empty", nameof(paymentProviderId));

            try
            {
                var placedBinanceOrder = await PlaceOrder(symbol, quantity, paymentProviderId);

                return new PlacedExchangeOrder
                {
                    Exchange = Name,
                    Symbol = symbol,
                    Side = Binance.Net.Enums.OrderSide.Buy.ToString(),
                    QuoteQuantity = placedBinanceOrder.QuoteQuantity,
                    QuoteQuantityFilled = placedBinanceOrder.QuoteQuantityFilled,
                    Price = placedBinanceOrder.AverageFillPrice,
                    QuantityFilled = placedBinanceOrder.QuantityFilled,
                    OrderId = placedBinanceOrder.Id,
                    Status = MapBinanceStatus(placedBinanceOrder.Status) ?? OrderStatus.Pending
                };
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to place spot market buy order for {Symbol}", symbol);
                throw; // Re-throw after logging
            }
        }

        /// <summary>
        /// Places a spot market sell order using the provided API credentials.
        /// </summary>
        /// <param name="symbol">The trading pair (e.g., BTCUSDT).</param>
        /// <param name="quantity">The amount of the base asset (e.g., BTC) to sell.</param>
        /// <param name="paymentProviderId">Unique ID for tracking this payment</param>
        /// <returns>The order details.</returns>
        public async Task<PlacedExchangeOrder> PlaceSpotMarketSellOrder(string symbol, decimal quantity, string paymentProviderId)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                throw new ArgumentException("Symbol cannot be null or empty", nameof(symbol));

            if (quantity <= 0)
                throw new ArgumentException("Quantity must be greater than zero", nameof(quantity));

            if (string.IsNullOrWhiteSpace(paymentProviderId))
                throw new ArgumentException("Payment provider ID cannot be null or empty", nameof(paymentProviderId));

            try
            {
                var placedBinanceOrder = await PlaceOrder(
                    symbol,
                    quantity,
                    paymentProviderId,
                    Binance.Net.Enums.OrderSide.Sell);

                return new PlacedExchangeOrder
                {
                    Exchange = Name,
                    Symbol = symbol,
                    Side = placedBinanceOrder.Side == Binance.Net.Enums.OrderSide.Buy ? OrderSide.Buy : OrderSide.Sell,
                    QuoteQuantity = placedBinanceOrder.QuoteQuantity,
                    QuoteQuantityFilled = placedBinanceOrder.QuoteQuantityFilled,
                    Price = placedBinanceOrder.AverageFillPrice,
                    QuantityFilled = placedBinanceOrder.QuantityFilled,
                    OrderId = placedBinanceOrder.Id,
                    Status = MapBinanceStatus(placedBinanceOrder.Status) ?? OrderStatus.Pending
                };
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to place spot market sell order for {Symbol}", symbol);
                throw; // Re-throw after logging
            }
        }

        /// <summary>
        /// Gets balances for specified tickers or all non-zero balances
        /// </summary>
        /// <param name="tickers">Optional list of specific tickers to get balances for</param>
        /// <returns>Collection of exchange balances</returns>
        public async Task<ResultWrapper<IEnumerable<ExchangeBalance>>> GetBalancesAsync(IEnumerable<string>? tickers = null)
        {
            try
            {
                _logger.LogInformation("Fetching exchange balances for ticker: {Ticker}",
                    tickers == null ? "All" : string.Join(", ", tickers));

                return await _retryPolicy.ExecuteAsync(async () =>
                {
                    var accountInfo = await _binanceClient.SpotApi.Account.GetAccountInfoAsync();
                    if (!accountInfo.Success || accountInfo.Data == null)
                    {
                        _logger.LogError("Failed to retrieve account info from Binance: {Error}",
                            accountInfo.Error?.Message);

                        throw new ExchangeApiException($"Unable to retrieve account info: {accountInfo.Error?.Message}", this.Name);
                    }

                    var balances = accountInfo.Data.Balances
                        .Where(b => b.Total > 0m); // Only include non-zero balances

                    if (tickers != null && tickers.Any())
                    {
                        // Normalize tickers for case-insensitive comparison
                        var normalizedTickers = tickers.Select(t => t.ToUpperInvariant()).ToHashSet();
                        balances = balances.Where(b => normalizedTickers.Contains(b.Asset.ToUpperInvariant()));
                    }

                    var result = balances.Select(b => new ExchangeBalance
                    {
                        Ticker = b.Asset,
                        Available = b.Available,
                        Locked = b.Locked,
                    }).ToList();

                    _logger.LogInformation("Successfully retrieved {Count} balance(s) from Binance", result.Count);
                    return ResultWrapper<IEnumerable<ExchangeBalance>>.Success(result);
                });
            }
            catch (Exception ex)
            {
                _logger.LogError("Error retrieving exchange balances: {Message}", ex.Message);
                return ResultWrapper<IEnumerable<ExchangeBalance>>.Failure(
                    FailureReason.ExchangeApiError,
                    $"Failed to retrieve exchange balances: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the balance for a specific ticker
        /// </summary>
        /// <param name="ticker">The ticker to get balance for</param>
        /// <returns>The exchange balance</returns>
        public async Task<ResultWrapper<ExchangeBalance>> GetBalanceAsync(string ticker)
        {
            if (string.IsNullOrWhiteSpace(ticker))
                throw new ArgumentException("Ticker cannot be null or empty", nameof(ticker));

            var balanceResult = await GetBalancesAsync(new[] { ticker });
            if (!balanceResult.IsSuccess)
            {
                return ResultWrapper<ExchangeBalance>.Failure(
                    balanceResult.Reason,
                    balanceResult.ErrorMessage);
            }

            var balance = balanceResult.Data.FirstOrDefault();

            // If no balance found, return zero balance
            if (balance == null)
            {
                _logger.LogInformation("No balance found for {Ticker}, returning zero balance", ticker);
                return ResultWrapper<ExchangeBalance>.Success(new ExchangeBalance
                {
                    Ticker = ticker,
                    Available = 0,
                    Locked = 0
                });
            }

            _logger.LogInformation("{Ticker} balance from Binance: Available={Available}, Locked={Locked}",
                ticker, balance.Available, balance.Locked);

            return ResultWrapper<ExchangeBalance>.Success(balance);
        }

        /// <summary>
        /// Checks if there is enough balance for a specified amount
        /// </summary>
        /// <param name="ticker">The ticker to check</param>
        /// <param name="amount">The amount to check against</param>
        /// <returns>True if enough balance is available, otherwise false</returns>
        public async Task<ResultWrapper<bool>> CheckBalanceHasEnough(string ticker, decimal amount)
        {
            if (string.IsNullOrWhiteSpace(ticker))
                throw new ArgumentException("Ticker cannot be null or empty", nameof(ticker));

            if (amount <= 0)
                throw new ArgumentException("Amount must be greater than zero", nameof(amount));

            try
            {
                var balanceResult = await GetBalanceAsync(ticker);
                if (!balanceResult.IsSuccess || balanceResult.Data is null)
                {
                    throw new ExchangeApiException($"Failed to fetch balance for {ticker}", this.Name);
                }

                var balance = balanceResult.Data;
                if (balance.Available < amount)
                {
                    _logger.LogWarning("Insufficient balance for {Exchange}:{Ticker}. Available: {Available}, Required: {Required}",
                        Name, ticker, balance.Available, amount);

                    return ResultWrapper<bool>.Success(false,
                        $"Insufficient balance for {Name}:{ticker}. Available: {balance.Available} Locked: {balance.Locked} Required: {amount}");
                }

                _logger.LogInformation("Sufficient balance for {Exchange}:{Ticker}. Available: {Available}, Required: {Required}",
                    Name, ticker, balance.Available, amount);

                return ResultWrapper<bool>.Success(true);
            }
            catch (Exception ex)
            {
                _logger.LogError("Error checking balance for {Ticker}: {Message}", ticker, ex.Message);
                return ResultWrapper<bool>.FromException(ex);
            }
        }

        /// <summary>
        /// Gets information about a specific order
        /// </summary>
        /// <param name="orderId">The order ID to get information for</param>
        /// <returns>The order details</returns>
        public async Task<PlacedExchangeOrder> GetOrderInfoAsync(long orderId)
        {
            if (orderId <= 0)
                throw new ArgumentException("Order ID must be greater than zero", nameof(orderId));

            try
            {
                // Find the order in the order history - we need the symbol
                // In a real implementation, we would look up the order by ID
                // For now, returning a placeholder until implemented
                _logger.LogWarning("GetOrderInfoAsync method is not fully implemented");

                return await Task.FromResult(new PlacedExchangeOrder()
                {
                    Exchange = Name,
                    Side = Binance.Net.Enums.OrderSide.Buy.ToString(), // Placeholder
                    OrderId = orderId,
                    Symbol = "BTCUSDT", // Placeholder
                    QuoteQuantity = 0,
                    QuoteQuantityFilled = 0,
                    QuantityFilled = 0,
                    Price = 0,
                    Status = OrderStatus.Filled // Placeholder
                });
            }
            catch (Exception ex)
            {
                _logger.LogError("Error retrieving order info for order ID {OrderId}: {Message}",
                    orderId, ex.Message);
                throw new OrderFetchException($"Failed to fetch order information: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets previous filled orders for a ticker and client order ID
        /// </summary>
        /// <param name="assetTicker">The asset ticker</param>
        /// <param name="clientOrderId">The client order ID</param>
        /// <returns>Collection of filled orders</returns>
        public async Task<ResultWrapper<IEnumerable<PlacedExchangeOrder>>> GetPreviousFilledOrders(
            string assetTicker, string clientOrderId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(assetTicker))
                    throw new ArgumentException("Asset ticker cannot be null or empty", nameof(assetTicker));

                if (string.IsNullOrWhiteSpace(clientOrderId))
                    throw new ArgumentException("Client order ID cannot be null or empty", nameof(clientOrderId));

                var ordersResult = await GetOrdersByClientOrderId(assetTicker, clientOrderId);
                if (ordersResult == null || !ordersResult.IsSuccess || ordersResult.Data == null)
                {
                    throw new OrderFetchException(ordersResult?.ErrorMessage ?? "Failed to fetch orders");
                }

                var filledOrders = ordersResult.Data.Where(o =>
                    o.Status == OrderStatus.Filled || o.Status == OrderStatus.PartiallyFilled);

                _logger.LogInformation("Found {Count} filled orders for client order ID {ClientOrderId}",
                    filledOrders.Count(), clientOrderId);

                return ResultWrapper<IEnumerable<PlacedExchangeOrder>>.Success(filledOrders);
            }
            catch (Exception ex)
            {
                _logger.LogError("Error retrieving previous filled orders: {Message}", ex.Message);
                return ResultWrapper<IEnumerable<PlacedExchangeOrder>>.FromException(ex);
            }
        }

        /// <summary>
        /// Gets orders by client order ID
        /// </summary>
        /// <param name="ticker">The ticker</param>
        /// <param name="clientOrderId">The client order ID</param>
        /// <returns>Collection of orders</returns>
        public async Task<ResultWrapper<IEnumerable<PlacedExchangeOrder>>> GetOrdersByClientOrderId(
            string ticker, string clientOrderId)
        {
            if (string.IsNullOrWhiteSpace(ticker))
                throw new ArgumentException("Ticker cannot be null or empty", nameof(ticker));

            if (string.IsNullOrWhiteSpace(clientOrderId))
                throw new ArgumentException("Client order ID cannot be null or empty", nameof(clientOrderId));

            var placedExchangeOrders = new List<PlacedExchangeOrder>();
            var symbol = $"{ticker}{_reserveAssetTicker}";

            try
            {
                _logger.LogInformation("Fetching exchange orders for clientOrderId: {ClientOrderId}", clientOrderId);

                return await _retryPolicy.ExecuteAsync(async () =>
                {
                    var binanceOrdersResult = await _binanceClient.SpotApi.Trading.GetOrdersAsync(symbol);
                    if (!binanceOrdersResult.Success || binanceOrdersResult.Data == null)
                    {
                        _logger.LogError("Failed to retrieve {Symbol} orders from Binance: {Error}",
                            symbol, binanceOrdersResult.Error?.Message);

                        throw new OrderFetchException(binanceOrdersResult.Error?.Message);
                    }

                    // Find orders with matching client order ID
                    var binanceOrders = binanceOrdersResult.Data
                        .Where(b => b.ClientOrderId == FormatClientOrderId(clientOrderId));

                    placedExchangeOrders = binanceOrders.Select(o => new PlacedExchangeOrder()
                    {
                        Exchange = Name,
                        Side = o.Side.ToString(),
                        Symbol = symbol,
                        QuoteQuantity = o.QuoteQuantity,
                        QuoteQuantityFilled = o.QuoteQuantityFilled,
                        Price = o.AverageFillPrice,
                        QuantityFilled = o.QuantityFilled,
                        OrderId = o.Id,
                        Status = MapBinanceStatus(o.Status) ?? OrderStatus.Pending,
                    }).ToList();

                    _logger.LogInformation("Successfully retrieved {Count} {Ticker} orders from Binance for ClientOrderId {ClientOrderId}",
                        placedExchangeOrders.Count, ticker, clientOrderId);

                    return ResultWrapper<IEnumerable<PlacedExchangeOrder>>.Success(placedExchangeOrders);
                });
            }
            catch (Exception ex)
            {
                _logger.LogError("Error retrieving exchange orders: {Message}", ex.Message);
                return ResultWrapper<IEnumerable<PlacedExchangeOrder>>.Failure(
                    FailureReason.ExchangeApiError,
                    $"Failed to retrieve exchange orders: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the current price for an asset
        /// </summary>
        /// <param name="ticker">The ticker to get price for</param>
        /// <returns>The current price</returns>
        public async Task<ResultWrapper<decimal>> GetAssetPrice(string ticker)
        {
            if (string.IsNullOrWhiteSpace(ticker))
                throw new ArgumentException("Ticker cannot be null or empty", nameof(ticker));

            try
            {
                var symbol = $"{ticker}{_reserveAssetTicker}";
                _logger.LogInformation("Fetching price for {Symbol}", symbol);

                return await _retryPolicy.ExecuteAsync(async () =>
                {
                    var binancePriceResult = await _binanceClient.SpotApi.ExchangeData.GetPriceAsync(symbol);
                    if (binancePriceResult is null || !binancePriceResult.Success || binancePriceResult.Data is null)
                    {
                        throw new Exception($"Failed to fetch Binance price for {ticker}: {binancePriceResult?.Error?.Message ?? "Binance price returned null."}");
                    }

                    _logger.LogInformation("Current price for {Symbol}: {Price}", symbol, binancePriceResult.Data.Price);
                    return ResultWrapper<decimal>.Success(binancePriceResult.Data.Price);
                });
            }
            catch (Exception ex)
            {
                _logger.LogError("Error fetching price for {Ticker}: {Message}", ticker, ex.Message);
                return ResultWrapper<decimal>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<Dictionary<string, decimal>>> GetAssetPrices(IEnumerable<string> tickers)
        {
            if (tickers.Count() == 0)
                throw new ArgumentException("Tickers cannot be empty", nameof(tickers));

                Dictionary<string, string> symbolsDict = new ();
            try
            {
                Dictionary<string, decimal> priceDict = new();
                foreach (var ticker in tickers)
                {
                    if (symbolsDict.ContainsKey(ticker)) continue;
                    var symbol = $"{ticker}{_reserveAssetTicker}";
                    symbolsDict.TryAdd(ticker, symbol);
                };

                _logger.LogInformation("Fetching prices for {Symbols}", string.Join(",", symbolsDict.Values));

                return await _retryPolicy.ExecuteAsync(async () =>
                {
                    var binancePriceResult = await _binanceClient.SpotApi.ExchangeData.GetPricesAsync(symbolsDict.Values);
                    if (binancePriceResult is null || !binancePriceResult.Success || binancePriceResult.Data is null)
                    {
                        throw new Exception($"Failed to fetch Binance prices for {string.Join(",", symbolsDict)}: {binancePriceResult?.Error?.Message ?? "Binance price returned null."}");
                    }

                    var prices = binancePriceResult.Data;

                    Dictionary<string, decimal> priceDict = new();

                    foreach (var ticker in symbolsDict.Keys)
                    {
                        priceDict.Add(ticker, prices.Where(p => p.Symbol.StartsWith(ticker)).Select(p => p.Price).FirstOrDefault());
                    }

                    return ResultWrapper<Dictionary<string, decimal>>.Success(priceDict);
                });
                

            }
            catch (Exception ex)
            {
                _logger.LogError("Error fetching prices for {Ticker}: {Message}", string.Join(",", symbolsDict.Keys), ex.Message);
                return ResultWrapper<Dictionary<string, decimal>>.FromException(ex);
            }
        }

        /// <summary>
        /// Gets the current price for an asset
        /// </summary>
        /// <param name="ticker">The ticker to get price for</param>
        /// <returns>The current price</returns>
        public async Task<ResultWrapper<decimal>> GetQuotePrice(string currency)
        {
            if (string.IsNullOrWhiteSpace(currency))
                throw new ArgumentException("Currency ticker cannot be null or empty", nameof(currency));

            try
            {
                var symbol = $"{_reserveAssetTicker}{currency}";
                _logger.LogInformation("Fetching price for {Symbol}", symbol);

                return await _retryPolicy.ExecuteAsync(async () =>
                {
                    var binancePriceResult = await _binanceClient.SpotApi.ExchangeData.GetPriceAsync(symbol);
                    if (binancePriceResult is null || !binancePriceResult.Success || binancePriceResult.Data is null)
                    {
                        throw new Exception($"Failed to fetch Binance price for {currency}: {binancePriceResult?.Error?.Message ?? "Binance price returned null."}");
                    }

                    _logger.LogInformation("Current price for {Symbol}: {Price}", symbol, binancePriceResult.Data.Price);
                    return ResultWrapper<decimal>.Success(binancePriceResult.Data.Price);
                });
            }
            catch (Exception ex)
            {
                _logger.LogError("Error fetching price for {Ticker}: {Message}", currency, ex.Message);
                return ResultWrapper<decimal>.FromException(ex);
            }
        }

        /// <summary>
        /// Gets the minimum notional value required to place an order for the specified ticker
        /// </summary>
        /// <param name="ticker">The ticker to get minimum notional for</param>
        /// <returns>The minimum notional value required for orders</returns>
        public async Task<ResultWrapper<decimal>> GetMinNotional(string ticker)
        {
            if (string.IsNullOrWhiteSpace(ticker))
                throw new ArgumentException("Ticker cannot be null or empty", nameof(ticker));

            try
            {
                var symbol = $"{ticker}{_reserveAssetTicker}";
                _logger.LogInformation("Fetching minimum notional for {Symbol}", symbol);

                return await _retryPolicy.ExecuteAsync(async () =>
                {
                    // Get exchange info which contains trading rules and filters
                    var exchangeInfoResult = await _binanceClient.SpotApi.ExchangeData.GetExchangeInfoAsync();
                    if (!exchangeInfoResult.Success || exchangeInfoResult.Data?.Symbols == null)
                    {
                        var errorMessage = $"Failed to fetch exchange info from Binance: {exchangeInfoResult.Error?.Message ?? "Exchange info returned null"}";
                        _logger.LogError(errorMessage);
                        throw new ExchangeApiException(errorMessage, Name);
                    }

                    // Find the specific symbol in the exchange info
                    var symbolInfo = exchangeInfoResult.Data.Symbols
                        .FirstOrDefault(s => s.Name.Equals(symbol, StringComparison.OrdinalIgnoreCase));

                    if (symbolInfo == null)
                    {
                        var errorMessage = $"Symbol {symbol} not found in Binance exchange info";
                        _logger.LogError(errorMessage);
                        throw new ExchangeApiException(errorMessage, Name);
                    }

                    // Extract the minimum notional value from the filter
                    var minNotional = symbolInfo.NotionalFilter.MinNotional;

                    if (minNotional <= 0)
                    {
                        _logger.LogWarning("Invalid minimum notional value ({MinNotional}) for {Symbol}, using default value of 10",
                            minNotional, symbol);
                        return ResultWrapper<decimal>.Success(10m); // Default fallback value
                    }

                    _logger.LogInformation("Minimum notional for {Symbol}: {MinNotional}", symbol, minNotional);
                    return ResultWrapper<decimal>.Success(minNotional);
                });
            }
            catch (Exception ex) when (ex is not ExchangeApiException)
            {
                var errorMessage = $"Error fetching minimum notional for {ticker}: {ex.Message}";
                _logger.LogError(errorMessage);
                return ResultWrapper<decimal>.FromException(
                    new ExchangeApiException(errorMessage, Name, ex));
            }
        }

        public async Task<ResultWrapper<Dictionary<string, decimal>>> GetMinNotionals(string[] tickers)
        {
            if (tickers.Any(t => string.IsNullOrWhiteSpace(t)))
                throw new ArgumentException("Ticker cannot be null or empty");

            try
            {
                var symbols = tickers.Select(t => $"{t}{_reserveAssetTicker}");
                _logger.LogInformation("Fetching minimum notional for {Symbols}", string.Join(',', symbols));

                return await _retryPolicy.ExecuteAsync(async () =>
                {
                    // Get exchange info which contains trading rules and filters
                    var exchangeInfoResult = await _binanceClient.SpotApi.ExchangeData.GetExchangeInfoAsync();
                    if (!exchangeInfoResult.Success || exchangeInfoResult.Data?.Symbols == null)
                    {
                        var errorMessage = $"Failed to fetch exchange info from Binance: {exchangeInfoResult.Error?.Message ?? "Exchange info returned null"}";
                        _logger.LogError(errorMessage);
                        throw new ExchangeApiException(errorMessage, Name);
                    }

                    // Find the specific symbol in the exchange info
                    var symbolsInfo = exchangeInfoResult.Data.Symbols
                        .Where(s => symbols.Contains(s.Name));

                    if (symbolsInfo == null || !symbolsInfo.Any())
                    {
                        var errorMessage = $"Symbols {string.Join(',', symbols)} not found in Binance exchange info";
                        _logger.LogError(errorMessage);
                        throw new ExchangeApiException(errorMessage, Name);
                    }

                    // Extract the minimum notional value from the filter
                    var minNotionals = new Dictionary<string, decimal>();
                    foreach (var s in symbolsInfo)
                    {
                        minNotionals.Add(s.Name, s.NotionalFilter?.MinNotional ?? 0m);
                    }

                    _logger.LogInformation("Minimum notionals retrieved for {Count} symbols", minNotionals.Count);
                    return ResultWrapper<Dictionary<string, decimal>>.Success(minNotionals);
                });
            }
            catch (Exception ex) when (ex is not ExchangeApiException)
            {
                var errorMessage = $"Error fetching minimum notional for tickers: {ex.Message}";
                _logger.LogError(errorMessage);
                return ResultWrapper<Dictionary<string, decimal>>.FromException(
                    new ExchangeApiException(errorMessage, Name, ex));
            }
        }
    }
}