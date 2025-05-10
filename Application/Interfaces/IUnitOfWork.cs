using Application.Interfaces;
using Application.Interfaces.Asset;
using Application.Interfaces.Exchange;
using Application.Interfaces.Payment;
using Application.Interfaces.Subscription;
using MongoDB.Driver;

namespace Domain.Interfaces
{
    /// <summary>
    /// Unit of Work interface that manages transactions across multiple repositories
    /// </summary>
    public interface IUnitOfWork : IDisposable
    {
        ISubscriptionRetryService SubscriptionRetry { get; }

        /// <summary>
        /// Begins a new MongoDB transaction
        /// </summary>
        Task<IClientSessionHandle> BeginTransactionAsync();

        /// <summary>
        /// Commits a MongoDB transaction
        /// </summary>
        Task CommitTransactionAsync(IClientSessionHandle session);

        /// <summary>
        /// Rolls back a MongoDB transaction
        /// </summary>
        Task RollbackTransactionAsync(IClientSessionHandle session);

        /// <summary>
        /// Asset service for managing crypto assets
        /// </summary>
        IAssetService Assets { get; }

        /// <summary>
        /// Subscription service for managing user subscriptions
        /// </summary>
        ISubscriptionService Subscriptions { get; }

        /// <summary>
        /// Balance service for managing user balances
        /// </summary>
        IBalanceService Balances { get; }

        /// <summary>
        /// Payment service for handling payment operations
        /// </summary>
        IPaymentService Payments { get; }

        /// <summary>
        /// Transaction service for recording financial transactions
        /// </summary>
        ITransactionService Transactions { get; }

        /// <summary>
        /// Exchange service for interacting with cryptocurrency exchanges
        /// </summary>
        IExchangeService Exchanges { get; }

        /// <summary>
        /// Event service for managing domain events
        /// </summary>
        IEventService Events { get; }

        /// <summary>
        /// Order management service for handling exchange orders
        /// </summary>
        IOrderManagementService OrderManagement { get; }

        /// <summary>
        /// Payment processing service for handling payment processing
        /// </summary>
        IPaymentProcessingService PaymentProcessing { get; }

        /// <summary>
        /// Balance management service for handling balance operations
        /// </summary>
        IBalanceManagementService BalanceManagement { get; }

        /// <summary>
        /// Order reconciliation service for reconciling exchange orders
        /// </summary>
        IOrderReconciliationService OrderReconciliation { get; }

        /// <summary>
        /// Commits all changes
        /// </summary>
        Task SaveChangesAsync();
    }
}