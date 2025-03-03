using Domain.Constants;
using Domain.DTOs;
using Domain.Interfaces;
using Domain.Models.Crypto;
using Domain.Models.Exchange;
using Domain.Models.Payment;
using MongoDB.Bson;

namespace Application.Interfaces
{
    public interface IExchangeService : IRepository<ExchangeOrderData>
    {
        public Task<AllocationOrdersResult> ProcessPayment(PaymentData transactionData);
        public Task<ResultWrapper<PlacedExchangeOrder>> PlaceExchangeOrderAsync(AssetData asset, decimal quantity, ObjectId subscriptionId, string side = OrderSide.Buy, string type = "MARKET");
        public Task<ResultWrapper<bool>> ResetBalances(IEnumerable<string>? tickers = null);
    }
}
