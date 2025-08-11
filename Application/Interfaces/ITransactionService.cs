using Application.Interfaces.Base;
using Domain.DTOs;
using Domain.DTOs.Subscription;
using Domain.Events;
using Domain.Events.Payment;
using Domain.Models.Transaction;
using MediatR;

namespace Application.Interfaces
{
    public interface ITransactionService : 
        IBaseService<TransactionData>,
        INotificationHandler<PaymentReceivedEvent>,
        INotificationHandler<ExchangeOrderCompletedEvent>
    {
        public Task<ResultWrapper<PaginatedResult<TransactionData>>> GetUserTransactionsAsync(
            Guid userId,
            int page = 1,
            int pageSize = 20);
        public Task<ResultWrapper<List<TransactionDto>>> GetBySubscriptionIdAsync(Guid subscriptionId);
    }
}
