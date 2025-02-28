using Binance.Net.Objects.Models.Spot;
using MongoDB.Bson;

namespace BinanceLibrary
{
    public interface IBinanceService
    {
        public Task<BinancePlacedOrder> PlaceSpotMarketBuyOrder(string symbol, decimal quantity, ObjectId subscriptionId);
        public Task<BinancePlacedOrder> PlaceSpotMarketSellOrder(string symbol, decimal quantity, ObjectId subscriptionId);
        public Task<decimal> GetFiatBalanceAsync(string symbol);
        public Task<BinancePlacedOrder> GetOrderInfoAsync(long orderId);
    }
}
