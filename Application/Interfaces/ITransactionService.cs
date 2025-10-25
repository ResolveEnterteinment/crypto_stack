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
        INotificationHandler<ExchangeOrderCompletedEvent>,
        INotificationHandler<WithdrawalApprovedEvent>
    {
        Task<ResultWrapper<TransactionData>> CreateTransactionAsync(
            TransactionData transaction,
            bool autoConfirm = true,
            CancellationToken cancellationToken = default);
        Task<ResultWrapper<PaginatedResult<TransactionData>>> GetUserTransactionsAsync(
            Guid userId,
            int page = 1,
            int pageSize = 20,
            bool includeAsReceiver = true);

        Task<ResultWrapper<List<TransactionData>>> GetSubscriptionTransactionsAsync(Guid subscriptionId);

        Task<ResultWrapper<List<TransactionData>>> GetBalanceHistoryAsync(
        Guid userId,
        Guid assetId,
        DateTime? fromDate = null,
        DateTime? toDate = null);
        Task<ResultWrapper<TransactionData>> ReverseTransactionAsync(
            Guid transactionId,
            string reason,
            CancellationToken cancellationToken = default);
    }
}
