using Binance.Net.Objects.Models.Spot;
using Domain.DTOs;
using MongoDB.Bson;

namespace BinanceLibrary
{
    public interface IBinanceService
    {
        public Task<PlacedExchangeOrder> PlaceSpotMarketBuyOrder(string symbol, decimal quantity, ObjectId subscriptionId);
        public Task<PlacedExchangeOrder> PlaceSpotMarketSellOrder(string symbol, decimal quantity, ObjectId subscriptionId);
        public Task<PlacedExchangeOrder> GetOrderInfoAsync(long orderId);
        public Task<ResultWrapper<IEnumerable<BinanceBalance>>> GetBalancesAsync(IEnumerable<string>? tickers = null);
        public Task<ResultWrapper<BinanceBalance>> GetBalanceAsync(string ticker);
    }
}
