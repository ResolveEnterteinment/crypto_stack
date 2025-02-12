using Binance.Net.Objects.Models.Spot;

namespace BinanceLibrary
{
    public interface IBinanceService
    {
        public Task<BinancePlacedOrder?> PlaceSpotMarketBuyOrder(string symbol, decimal quantity);
        public Task<BinancePlacedOrder?> PlaceSpotMarketSellOrder(string symbol, decimal quantity);
    }
}
