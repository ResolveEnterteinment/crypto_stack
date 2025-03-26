using Application.Interfaces.Exchange;
using Binance.Net;
using Binance.Net.Clients;
using Binance.Net.Objects.Models.Spot;
using CryptoExchange.Net.Authentication;
using Domain.Constants;
using Domain.DTOs;
using Domain.DTOs.Exchange;
using Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace BinanceLibrary
{
    public class BinanceService : IExchange
    {
        private string _reserveAssetTicker;
        public string Name { get => "BINANCE"; }

        public string ReserveAssetTicker { get => _reserveAssetTicker; set => throw new NotImplementedException(); }

        private readonly BinanceRestClient _binanceClient;
        private string? MapBinanceStatus(Binance.Net.Enums.OrderStatus status) => status switch
        {
            Binance.Net.Enums.OrderStatus.PendingNew or Binance.Net.Enums.OrderStatus.PendingNew => OrderStatus.Pending,
            Binance.Net.Enums.OrderStatus.Filled => OrderStatus.Filled,
            Binance.Net.Enums.OrderStatus.PartiallyFilled => OrderStatus.PartiallyFilled,
            Binance.Net.Enums.OrderStatus.Canceled or Binance.Net.Enums.OrderStatus.Rejected or Binance.Net.Enums.OrderStatus.Expired => OrderStatus.Failed,
            _ => null
        };

        protected readonly ILogger _logger;
        public BinanceService(ExchangeSettings binanceSettings, ILogger logger)
        {
            // Replace these with your Binance API credentials
            string apiKey = binanceSettings.ApiKey;
            string apiSecret = binanceSettings.ApiSecret;
            bool isTestnet = binanceSettings.IsTestnet;
            _reserveAssetTicker = binanceSettings.ReserveStableAssetTicker;
            _logger = logger;

            _binanceClient = new BinanceRestClient(options =>
            {
                options.ApiCredentials = new ApiCredentials(apiKey, apiSecret);
                options.Environment = isTestnet ? BinanceEnvironment.Testnet : BinanceEnvironment.Live;
            });
        }

        internal async Task<BinancePlacedOrder> PlaceOrder(string symbol, decimal quantity, string paymentProviderId, Binance.Net.Enums.OrderSide side = Binance.Net.Enums.OrderSide.Buy, Binance.Net.Enums.SpotOrderType type = Binance.Net.Enums.SpotOrderType.Market)
        {
            try
            {
                var orderResult = await _binanceClient.SpotApi.Trading.PlaceOrderAsync(
                symbol,
                side,
                type,
                quantity: side == Binance.Net.Enums.OrderSide.Sell ? quantity : null, // Not used for market buy orders.
                quoteQuantity: side == Binance.Net.Enums.OrderSide.Buy ? quantity : null,
                newClientOrderId: paymentProviderId.ToString()) //subscription id to track user orders
                .ConfigureAwait(false);
                if (!orderResult.Success)
                {
                    throw new Exception(orderResult.Error?.Message);
                }
                return orderResult.Data;
            }
            catch (Exception ex)
            {
                throw new Exception($"Error placing Binance order: {ex.Message}");
            }
        }

        /// <summary>
        /// Places a spot market buy order using the provided API credentials.
        /// For spot market orders on Binance, the amount to be spent is provided via the quoteOrderQuantity parameter.
        /// </summary>
        /// <param name="symbol">The trading pair (e.g., BTCUSDT).</param>
        /// <param name="quantity">The amount of the quote asset (e.g., USDT) to spend.</param>
        /// <returns>The order details if successful; otherwise, null.</returns>
        public async Task<PlacedExchangeOrder> PlaceSpotMarketBuyOrder(string symbol, decimal quantity, string paymentProviderId)
        {
            try
            {
                var placedBinanceOrder = await PlaceOrder(symbol, quantity, paymentProviderId);
                var placedOrder = new PlacedExchangeOrder()
                {
                    Symbol = symbol,
                    QuoteQuantity = placedBinanceOrder.QuoteQuantity,
                    QuoteQuantityFilled = placedBinanceOrder.QuoteQuantityFilled,
                    Price = placedBinanceOrder.AverageFillPrice,
                    QuantityFilled = placedBinanceOrder.QuantityFilled,
                    OrderId = placedBinanceOrder.Id,
                    Status = MapBinanceStatus(placedBinanceOrder.Status),
                };
                return placedOrder;
            }
            catch (Exception)
            {
                throw;
            }

        }
        /// <summary>
        /// Places a spot market sell order using the provided API credentials.
        /// For spot market orders on Binance, the amount to be spent is provided via the quoteOrderQuantity parameter.
        /// </summary>
        /// <param name="symbol">The trading pair (e.g., BTCUSDT).</param>
        /// <param name="quantity">The amount of the quote asset (e.g., USDT) to spend.</param>
        /// <returns>The order details if successful; otherwise, null.</returns>
        public async Task<PlacedExchangeOrder> PlaceSpotMarketSellOrder(string symbol, decimal quantity, string paymentProviderId)
        {
            try
            {
                var placedBinanceOrder = await PlaceOrder(symbol, quantity, paymentProviderId, Binance.Net.Enums.OrderSide.Sell);
                var placedOrder = new PlacedExchangeOrder()
                {
                    Symbol = symbol,
                    QuoteQuantity = placedBinanceOrder.QuoteQuantity,
                    QuoteQuantityFilled = placedBinanceOrder.QuoteQuantityFilled,
                    Price = placedBinanceOrder.AverageFillPrice,
                    QuantityFilled = placedBinanceOrder.QuantityFilled,
                    OrderId = placedBinanceOrder.Id,
                    Status = MapBinanceStatus(placedBinanceOrder.Status),
                };
                return placedOrder;
            }
            catch (Exception)
            {
                throw;
            }
        }
        public async Task<ResultWrapper<IEnumerable<ExchangeBalance>>> GetBalancesAsync(IEnumerable<string>? tickers = null)
        {
            try
            {
                _logger.LogInformation("Fetching exchange balances for ticker: {Ticker}", tickers == null ? "All" : string.Join(", ", tickers));
                var accountInfo = await _binanceClient.SpotApi.Account.GetAccountInfoAsync();
                if (!accountInfo.Success || accountInfo.Data == null)
                {
                    _logger.LogError("Failed to retrieve account info from Binance: {Error}", accountInfo.Error?.Message);
                    throw new Exception($"Unable to retrieve account info: {accountInfo.Error?.Message}");
                }

                var balances = accountInfo.Data.Balances
                    .Where(b => b.Total > 0m); // Only include non-zero balances

                if (tickers != null && tickers.Any())
                {
                    balances = balances.Where(b => tickers.Contains(b.Asset));
                }

                var result = balances.ToList().Select(b => new ExchangeBalance()
                {
                    Available = b.Available,
                    Locked = b.Locked,
                });
                _logger.LogInformation($"Successfully retrieved {result.ToList().Count} balance(s) from Binance");
                return ResultWrapper<IEnumerable<ExchangeBalance>>.Success(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving exchange balances: {Message}", ex.Message);
                return ResultWrapper<IEnumerable<ExchangeBalance>>.Failure(FailureReason.ExchangeApiError, $"Failed to retrieve exchange balances: {ex.Message}");
            }
        }
        public async Task<ResultWrapper<ExchangeBalance>> GetBalanceAsync(string symbol)
        {
            //throw new NotImplementedException();
            var balanceResult = await GetBalancesAsync(new List<string>().Append(symbol));
            if (!balanceResult.IsSuccess)
            {
                return ResultWrapper<ExchangeBalance>.Failure(balanceResult.Reason, balanceResult.ErrorMessage);
            }
            var balance = balanceResult.Data.FirstOrDefault();
            _logger.LogInformation($"{symbol} balance: from Binance: {balance}");
            return ResultWrapper<ExchangeBalance>.Success(new ExchangeBalance()
            {
                Available = balance.Available,
                Locked = balance.Locked,
            });
        }
        public async Task<ResultWrapper<bool>> CheckBalanceHasEnough(string ticker, decimal amount)
        {
            try
            {
                var balanceResult = await GetBalanceAsync(ticker);
                if (!balanceResult.IsSuccess || balanceResult.Data is null)
                {
                    throw new BalanceFetchException($"Failed to fetch balance for {this.Name}");
                }
                var balance = balanceResult.Data;
                if (balance.Available < amount)
                {
                    return ResultWrapper<bool>.Success(false, $"Insufficient balance for {this.Name}:{ticker}. Available: {balance.Available} Locked: {balance.Locked} Required: {amount}");
                    //throw new InsufficientBalanceException($"Insufficient balance for {this.Name}:{ticker}. Available: {balance.Available} Locked: {balance.Locked} Required: {amount}");
                }
                return ResultWrapper<bool>.Success(true);
            }
            catch (Exception ex)
            {
                return ResultWrapper<bool>.FromException(ex);
            }
        }

        public async Task<PlacedExchangeOrder> GetOrderInfoAsync(long orderId)
        {
            //throw new NotImplementedException();
            return await Task.FromResult(new PlacedExchangeOrder()
            {
                OrderId = orderId,
                Symbol = "BTCUSDT",
                QuoteQuantity = 0,
                QuoteQuantityFilled = 0,
                QuantityFilled = 0,
                Price = 0,
                Status = "Filled"
            });
        }

        public async Task<ResultWrapper<IEnumerable<PlacedExchangeOrder>>> GetPreviousFilledOrders(string assetTicker, string clientOrderId)
        {
            try
            {
                var ordersResult = await GetOrdersByClientOrderId(assetTicker, clientOrderId);
                if (ordersResult == null || !ordersResult.IsSuccess || ordersResult.Data == null)
                {
                    throw new OrderFetchException(ordersResult.ErrorMessage);
                }
                var filledOrders = ordersResult.Data.Where(o => o.Status == OrderStatus.Filled || o.Status == OrderStatus.PartiallyFilled);

                return ResultWrapper<IEnumerable<PlacedExchangeOrder>>.Success(filledOrders);
            }
            catch (Exception ex)
            {
                return ResultWrapper<IEnumerable<PlacedExchangeOrder>>.FromException(ex);
            }

        }

        public async Task<ResultWrapper<IEnumerable<PlacedExchangeOrder>>> GetOrdersByClientOrderId(string ticker, string clientOrderId)
        {
            var placedExchangeOrders = new List<PlacedExchangeOrder>();
            var symbol = $"{ticker}USDC";
            try
            {
                _logger.LogInformation("Fetching exchange orders for clientOrderId: {ClientOrderId}", clientOrderId);
                var binanceOrdersResult = await _binanceClient.SpotApi.Trading.GetOrdersAsync(symbol);
                if (!binanceOrdersResult.Success || binanceOrdersResult.Data == null)
                {
                    _logger.LogError($"Failed to retrieve {symbol} orders from Binance: {binanceOrdersResult.Error?.Message}");
                    throw new Exception(binanceOrdersResult.Error?.Message);
                }

                var binanceOrders = binanceOrdersResult.Data
                    .Where(b => b.ClientOrderId == clientOrderId); // Only include non-zero balances

                placedExchangeOrders = binanceOrders.Select(o => new PlacedExchangeOrder()
                {
                    Symbol = symbol,
                    QuoteQuantity = o.QuoteQuantity,
                    QuoteQuantityFilled = o.QuoteQuantityFilled,
                    Price = o.AverageFillPrice,
                    QuantityFilled = o.QuantityFilled,
                    OrderId = o.Id,
                    Status = MapBinanceStatus(o.Status),
                }).ToList();

                _logger.LogInformation("Successfully retrieved {Ticker} orders from Binance for ClientOrderId {ClientOrderId}", ticker, clientOrderId);
                return ResultWrapper<IEnumerable<PlacedExchangeOrder>>.Success(placedExchangeOrders);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving exchange balances: {Message}", ex.Message);
                return ResultWrapper<IEnumerable<PlacedExchangeOrder>>.Failure(FailureReason.ExchangeApiError, $"Failed to retrieve exchange orders: {ex.Message}");
            }
        }

        public async Task<ResultWrapper<decimal>> GetAssetPrice(string ticker)
        {
            try
            {
                var symbol = $"{ticker}{_reserveAssetTicker}";
                var binancePriceResult = await _binanceClient.SpotApi.ExchangeData.GetPriceAsync(symbol);
                if (binancePriceResult is null || !binancePriceResult.Success || binancePriceResult.Data is null)
                {
                    throw new Exception($"Failed to fetch Binance price for {ticker}: {binancePriceResult?.Error?.Message ?? "Binance price returned null."}");
                }
                return ResultWrapper<decimal>.Success(binancePriceResult.Data.Price);
            }
            catch (Exception ex)
            {
                return ResultWrapper<decimal>.FromException(ex);
            }

        }
    }
}
