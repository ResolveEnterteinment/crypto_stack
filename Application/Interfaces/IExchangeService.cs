using Domain.Constants;
using Domain.DTOs;
using Domain.Interfaces;
using Domain.Models.Exchange;
using Domain.Models.Payment;
using MongoDB.Bson;

namespace Application.Interfaces
{
    public interface IExchangeService : IRepository<ExchangeOrderData>
    {
        public Task<ResultWrapper<IEnumerable<OrderResult>>> ProcessPayment(PaymentData transactionData);
        public Task<ResultWrapper<PlacedExchangeOrder>> PlaceExchangeOrderAsync(Guid assetId, string assetTicker, decimal quantity, Guid subscriptionId, string side = OrderSide.Buy, string type = "MARKET");
        public Task<ResultWrapper<bool>> ResetBalances(IEnumerable<string>? tickers = null);
    }
}
