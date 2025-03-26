using Domain.DTOs;
using Domain.DTOs.Exchange;

namespace Application.Interfaces.Exchange
{
    public interface IExchange
    {
        public string Name { get; }
        public string ReserveAssetTicker { get; set; }
        public Task<PlacedExchangeOrder> PlaceSpotMarketBuyOrder(string symbol, decimal quantity, string clientOrderId);
        public Task<PlacedExchangeOrder> PlaceSpotMarketSellOrder(string symbol, decimal quantity, string clientOrderId);
        public Task<PlacedExchangeOrder> GetOrderInfoAsync(long orderId);
        public Task<ResultWrapper<IEnumerable<PlacedExchangeOrder>>> GetPreviousFilledOrders(string ticker, string clientOrderId);
        public Task<ResultWrapper<IEnumerable<ExchangeBalance>>> GetBalancesAsync(IEnumerable<string>? tickers = null);
        public Task<ResultWrapper<ExchangeBalance>> GetBalanceAsync(string ticker);
        public Task<ResultWrapper<bool>> CheckBalanceHasEnough(string ticker, decimal amount);
        public Task<ResultWrapper<IEnumerable<PlacedExchangeOrder>>> GetOrdersByClientOrderId(string clientOrderId, string? ticker = null);
        public Task<ResultWrapper<decimal>> GetAssetPrice(string ticker);
    }
}
