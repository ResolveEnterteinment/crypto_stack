using Domain.DTOs;
using Domain.DTOs.Subscription;
using Domain.Models.Transaction;

namespace Application.Interfaces
{
    public interface ITransactionService : IRepository<TransactionData>
    {
        public Task<ResultWrapper<PaginatedResult<TransactionData>>> GetUserTransactionsAsync(
            Guid userId,
            int page = 1,
            int pageSize = 20);
        public Task<ResultWrapper<IEnumerable<TransactionDto>>> GetBySubscriptionIdAsync(Guid subscriptionId);
        public Task<ResultWrapper<IEnumerable<TransactionData>>> GetByPaymentProviderIdAsync(string paymentProviderId);
        public Task<ResultWrapper<Guid>> CreateTransactionAsync(TransactionData transaction);
    }
}
