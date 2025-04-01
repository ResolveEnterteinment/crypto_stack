using Domain.Constants;
using Domain.DTOs;
using Domain.DTOs.Exchange;
using Domain.Models.Asset;
using Domain.Models.Payment;

namespace Application.Interfaces.Exchange
{
    public interface IOrderManagementService
    {
        public Task<ResultWrapper<PlacedExchangeOrder>> PlaceExchangeOrderAsync(IExchange exchange, string assetTicker, decimal quantity, string paymentProviderId, string side = OrderSide.Buy, string type = "MARKET");
        public Task<ResultWrapper<decimal>> GetPreviousOrdersSum(IExchange exchange, AssetData asset, PaymentData payment);
        public Task HandleDustAsync(PlacedExchangeOrder order);
    }
}
