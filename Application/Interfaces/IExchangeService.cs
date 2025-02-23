using Domain.Constants;
using Domain.DTOs;
using Domain.Models.Crypto;
using Domain.Models.Transaction;

namespace Application.Interfaces
{
    public interface IExchangeService
    {
        public Task<AllocationOrdersResult> ProcessTransaction(TransactionData transactionData);
        public Task<PlacedOrderResult?> PlaceExchangeOrderAsync(CoinData coin, decimal quantity, string side = OrderSide.Buy, string type = "MARKET");
    }
}
