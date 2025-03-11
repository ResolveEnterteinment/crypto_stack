using Binance.Net.Objects.Models.Spot;
using Domain.DTOs;

namespace BinanceLibrary
{
    public interface IBinanceService
    {
        public Task<PlacedExchangeOrder> PlaceSpotMarketBuyOrder(string symbol, decimal quantity, string subscriptionId);
        public Task<PlacedExchangeOrder> PlaceSpotMarketSellOrder(string symbol, decimal quantity, string subscriptionId);
        public Task<PlacedExchangeOrder> GetOrderInfoAsync(long orderId);
        public Task<ResultWrapper<IEnumerable<BinanceBalance>>> GetBalancesAsync(IEnumerable<string>? tickers = null);
        public Task<ResultWrapper<BinanceBalance>> GetBalanceAsync(string ticker);
        public Task<ResultWrapper<IEnumerable<PlacedExchangeOrder>>> GetOrdersByClientOrderId(string clientOrderId, string? ticker = null);
    }
}
