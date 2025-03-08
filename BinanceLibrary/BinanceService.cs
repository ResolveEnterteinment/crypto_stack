using Binance.Net;
using Binance.Net.Clients;
using Binance.Net.Objects.Models.Spot;
using CryptoExchange.Net.Authentication;
using Domain.Constants;
using Domain.DTOs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;

namespace BinanceLibrary
{
    public class BinanceService : IBinanceService
    {
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
        public BinanceService(IOptions<BinanceSettings> binanceSettings, ILogger logger)
        {
            // Replace these with your Binance API credentials
            string apiKey = binanceSettings.Value.ApiKey;
            string apiSecret = binanceSettings.Value.ApiSecret;
            bool isTestnet = binanceSettings.Value.IsTestnet;
            _logger = logger;

            _binanceClient = new BinanceRestClient(options =>
            {
                options.ApiCredentials = new ApiCredentials(apiKey, apiSecret);
                options.Environment = isTestnet ? BinanceEnvironment.Testnet : BinanceEnvironment.Live;
            });
        }

        internal async Task<BinancePlacedOrder> PlaceOrder(string symbol, decimal quantity, ObjectId subscriptionId, Binance.Net.Enums.OrderSide side = Binance.Net.Enums.OrderSide.Buy, Binance.Net.Enums.SpotOrderType type = Binance.Net.Enums.SpotOrderType.Market)
        {
            try
            {
                var orderResult = await _binanceClient.SpotApi.Trading.PlaceOrderAsync(
                symbol,
                side,
                type,
                quantity: side == Binance.Net.Enums.OrderSide.Sell ? quantity : null, // Not used for market buy orders.
                quoteQuantity: side == Binance.Net.Enums.OrderSide.Buy ? quantity : null,
                newClientOrderId: subscriptionId.ToString()) //subscription id to track user orders
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
        public async Task<PlacedExchangeOrder> PlaceSpotMarketBuyOrder(string symbol, decimal quantity, ObjectId subscriptionId)
        {
            try
            {
                var placedBinanceOrder = await PlaceOrder(symbol, quantity, subscriptionId);
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
        public async Task<PlacedExchangeOrder> PlaceSpotMarketSellOrder(string symbol, decimal quantity, ObjectId subscriptionId)
        {
            try
            {
                var placedBinanceOrder = await PlaceOrder(symbol, quantity, subscriptionId, Binance.Net.Enums.OrderSide.Sell);
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
        public async Task<ResultWrapper<IEnumerable<BinanceBalance>>> GetBalancesAsync(IEnumerable<string>? tickers = null)
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

                var result = balances.ToList();
                _logger.LogInformation("Successfully retrieved {Count} balance(s) from Binance", result.Count);
                return ResultWrapper<IEnumerable<BinanceBalance>>.Success(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving exchange balances: {Message}", ex.Message);
                return ResultWrapper<IEnumerable<BinanceBalance>>.Failure(FailureReason.ExchangeApiError, $"Failed to retrieve exchange balances: {ex.Message}");
            }
        }
        public async Task<ResultWrapper<BinanceBalance>> GetBalanceAsync(string symbol)
        {
            //throw new NotImplementedException();
            var balanceResult = await GetBalancesAsync(new List<string>().Append(symbol));
            if (!balanceResult.IsSuccess)
            {
                return ResultWrapper<BinanceBalance>.Failure(balanceResult.FailureReason, balanceResult.ErrorMessage);
            }
            var balance = balanceResult.Data.FirstOrDefault();
            _logger.LogInformation($"{symbol} balance: from Binance: {balance}");
            return ResultWrapper<BinanceBalance>.Success(balance);
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
    }
}
