using Application.Contracts.Responses.Exchange;
using Domain.Models.Transaction;

namespace Application.Interfaces
{
    public interface IExchangeService
    {
        public Task<IEnumerable<ExchangeOrderResponse>?> ProcessTransaction(TransactionData transactionData);
    }
}
