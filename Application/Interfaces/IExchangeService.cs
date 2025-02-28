using Domain.Constants;
using Domain.DTOs;
using Domain.Models.Crypto;
using Domain.Models.Transaction;
using MongoDB.Bson;

namespace Application.Interfaces
{
    public interface IExchangeService
    {
        public Task<AllocationOrdersResult> ProcessTransaction(TransactionData transactionData);
        public Task<ResultWrapper<PlacedExchangeOrder>> PlaceExchangeOrderAsync(CoinData coin, decimal quantity, ObjectId subscriptionId, string side = OrderSide.Buy, string type = "MARKET");
    }
}
