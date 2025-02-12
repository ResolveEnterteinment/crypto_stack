using Binance.Net.Clients;
using Binance.Net.Objects.Models.Spot;
using CryptoExchange.Net.Authentication;
using Domain.DTOs;
using Microsoft.Extensions.Options;

namespace BinanceLibrary
{
    public class BinanceService : IBinanceService
    {
        private BinanceRestClient _binanceClient;
        public BinanceService(IOptions<BinanceSettings> binanceSettings)
        {

            // Replace these with your Binance API credentials
            string apiKey = binanceSettings.Value.ApiKey;
            string apiSecret = binanceSettings.Value.ApiSecret;

            _binanceClient = new BinanceRestClient();
            _binanceClient.SetApiCredentials(new ApiCredentials(binanceSettings.Value.ApiKey, binanceSettings.Value.ApiSecret));
        }

        internal async Task<BinancePlacedOrder?> PlaceOrder(string symbol, decimal quantity, Binance.Net.Enums.OrderSide side = Binance.Net.Enums.OrderSide.Buy, Binance.Net.Enums.SpotOrderType type = Binance.Net.Enums.SpotOrderType.Market)
        {

            var orderResult = await _binanceClient.SpotApi.Trading.PlaceOrderAsync(
                symbol,
                side,
                type,
                quantity: 0, // Not used for market buy orders.
                quoteQuantity: quantity)
                .ConfigureAwait(false);
            if (!orderResult.Success)
            {
                Console.WriteLine("Error placing order: " + orderResult.Error?.Message);
                return null;
            }

            return orderResult.Data;

        }

        /// <summary>
        /// Places a spot market buy order using the provided API credentials.
        /// For spot market orders on Binance, the amount to be spent is provided via the quoteOrderQuantity parameter.
        /// </summary>
        /// <param name="symbol">The trading pair (e.g., BTCUSDT).</param>
        /// <param name="quantity">The amount of the quote asset (e.g., USDT) to spend.</param>
        /// <returns>The order details if successful; otherwise, null.</returns>
        public async Task<BinancePlacedOrder?> PlaceSpotMarketBuyOrder(string symbol, decimal quantity)
        {
            return await PlaceOrder(symbol, quantity);
        }
        /// <summary>
        /// Places a spot market sell order using the provided API credentials.
        /// For spot market orders on Binance, the amount to be spent is provided via the quoteOrderQuantity parameter.
        /// </summary>
        /// <param name="symbol">The trading pair (e.g., BTCUSDT).</param>
        /// <param name="quantity">The amount of the quote asset (e.g., USDT) to spend.</param>
        /// <returns>The order details if successful; otherwise, null.</returns>
        public async Task<BinancePlacedOrder?> PlaceSpotMarketSellOrder(string symbol, decimal quantity)
        {
            return await PlaceOrder(symbol, quantity, Binance.Net.Enums.OrderSide.Sell);
        }
    }
}
