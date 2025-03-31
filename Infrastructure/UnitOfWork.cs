using Application.Interfaces;
using Application.Interfaces.Exchange;
using Application.Interfaces.Payment;
using Domain.Interfaces;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Infrastructure
{
    /// <summary>
    /// Implementation of the Unit of Work pattern for MongoDB
    /// </summary>
    public class UnitOfWork : IUnitOfWork
    {
        private readonly IMongoClient _mongoClient;
        private readonly ILogger<UnitOfWork> _logger;
        private bool _disposed = false;

        // Services
        private readonly IAssetService _assetService;
        private readonly ISubscriptionService _subscriptionService;
        private readonly IBalanceService _balanceService;
        private readonly IPaymentService _paymentService;
        private readonly ITransactionService _transactionService;
        private readonly IExchangeService _exchangeService;
        private readonly IEventService _eventService;
        private readonly IOrderManagementService _orderManagementService;
        private readonly IPaymentProcessingService _paymentProcessingService;
        private readonly IBalanceManagementService _balanceManagementService;
        private readonly IOrderReconciliationService _orderReconciliationService;

        public UnitOfWork(
            IMongoClient mongoClient,
            ILogger<UnitOfWork> logger,
            IAssetService assetService,
            ISubscriptionService subscriptionService,
            IBalanceService balanceService,
            IPaymentService paymentService,
            ITransactionService transactionService,
            IExchangeService exchangeService,
            IEventService eventService,
            IOrderManagementService orderManagementService,
            IPaymentProcessingService paymentProcessingService,
            IBalanceManagementService balanceManagementService,
            IOrderReconciliationService orderReconciliationService)
        {
            _mongoClient = mongoClient ?? throw new ArgumentNullException(nameof(mongoClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
            _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
            _balanceService = balanceService ?? throw new ArgumentNullException(nameof(balanceService));
            _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
            _transactionService = transactionService ?? throw new ArgumentNullException(nameof(transactionService));
            _exchangeService = exchangeService ?? throw new ArgumentNullException(nameof(exchangeService));
            _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
            _orderManagementService = orderManagementService ?? throw new ArgumentNullException(nameof(orderManagementService));
            _paymentProcessingService = paymentProcessingService ?? throw new ArgumentNullException(nameof(paymentProcessingService));
            _balanceManagementService = balanceManagementService ?? throw new ArgumentNullException(nameof(balanceManagementService));
            _orderReconciliationService = orderReconciliationService ?? throw new ArgumentNullException(nameof(orderReconciliationService));
        }

        // Properties exposing the services
        public IAssetService Assets => _assetService;
        public ISubscriptionService Subscriptions => _subscriptionService;
        public IBalanceService Balances => _balanceService;
        public IPaymentService Payments => _paymentService;
        public ITransactionService Transactions => _transactionService;
        public IExchangeService Exchanges => _exchangeService;
        public IEventService Events => _eventService;
        public IOrderManagementService OrderManagement => _orderManagementService;
        public IPaymentProcessingService PaymentProcessing => _paymentProcessingService;
        public IBalanceManagementService BalanceManagement => _balanceManagementService;
        public IOrderReconciliationService OrderReconciliation => _orderReconciliationService;

        /// <summary>
        /// Begins a new transaction
        /// </summary>
        public async Task<IClientSessionHandle> BeginTransactionAsync()
        {
            try
            {
                var session = await _mongoClient.StartSessionAsync();
                session.StartTransaction();
                return session;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to begin transaction");
                throw;
            }
        }

        /// <summary>
        /// Commits a transaction
        /// </summary>
        public async Task CommitTransactionAsync(IClientSessionHandle session)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            try
            {
                if (session.IsInTransaction)
                    await session.CommitTransactionAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to commit transaction");
                throw;
            }
        }

        /// <summary>
        /// Rolls back a transaction
        /// </summary>
        public async Task RollbackTransactionAsync(IClientSessionHandle session)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            try
            {
                if (session.IsInTransaction)
                    await session.AbortTransactionAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to rollback transaction");
                throw;
            }
        }

        /// <summary>
        /// Executes a function within a transaction, handling commit and rollback
        /// </summary>
        public async Task<TResult> ExecuteInTransactionAsync<TResult>(Func<IClientSessionHandle, Task<TResult>> action)
        {
            using var session = await BeginTransactionAsync();
            try
            {
                var result = await action(session);
                await CommitTransactionAsync(session);
                return result;
            }
            catch (Exception)
            {
                await RollbackTransactionAsync(session);
                throw;
            }
        }

        /// <summary>
        /// Executes a function within a transaction with no return value
        /// </summary>
        public async Task ExecuteInTransactionAsync(Func<IClientSessionHandle, Task> action)
        {
            using var session = await BeginTransactionAsync();
            try
            {
                await action(session);
                await CommitTransactionAsync(session);
            }
            catch (Exception)
            {
                await RollbackTransactionAsync(session);
                throw;
            }
        }

        /// <summary>
        /// Saves all changes
        /// </summary>
        public async Task SaveChangesAsync()
        {
            // For MongoDB, changes are saved immediately
            // This method is provided for consistency with the UoW pattern
            await Task.CompletedTask;
        }

        /// <summary>
        /// Disposes the unit of work
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Protected implementation of Dispose pattern
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                // Dispose managed resources if any
            }

            _disposed = true;
        }
    }
}