using Binance.Net;
using Binance.Net.Clients;
using Binance.Net.Objects.Models.Spot;
using CryptoExchange.Net.Authentication;
using Domain.DTOs;
using Microsoft.Extensions.Options;
using MongoDB.Bson;

namespace BinanceLibrary
{
    public class BinanceService : IBinanceService
    {
        private readonly BinanceRestClient _binanceClient;
        public BinanceService(IOptions<BinanceSettings> binanceSettings)
        {
            // Replace these with your Binance API credentials
            string apiKey = binanceSettings.Value.ApiKey;
            string apiSecret = binanceSettings.Value.ApiSecret;
            bool isTestnet = binanceSettings.Value.IsTestnet;

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
                //quantity: 0, // Not used for market buy orders.
                quoteQuantity: quantity,
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
        public async Task<BinancePlacedOrder> PlaceSpotMarketBuyOrder(string symbol, decimal quantity, ObjectId subscriptionId)
        {
            try
            {
                return await PlaceOrder(symbol, quantity, subscriptionId);
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
        public async Task<BinancePlacedOrder> PlaceSpotMarketSellOrder(string symbol, decimal quantity, ObjectId subscriptionId)
        {
            try
            {
                return await PlaceOrder(symbol, quantity, subscriptionId, Binance.Net.Enums.OrderSide.Sell);
            }
            catch (Exception)
            {

                throw;
            }
        }

        Task<decimal> IBinanceService.GetFiatBalanceAsync(string symbol)
        {
            //throw new NotImplementedException();
            return Task.FromResult(1000m);
        }

        Task<BinancePlacedOrder> IBinanceService.GetOrderInfoAsync(long orderId)
        {
            //throw new NotImplementedException();
            return Task.FromResult(new BinancePlacedOrder() { });
        }
    }
}
