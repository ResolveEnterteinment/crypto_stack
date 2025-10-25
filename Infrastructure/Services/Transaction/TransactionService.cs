using Application.Interfaces;
using Application.Interfaces.Asset;
using Application.Interfaces.Payment;
using Domain.Constants.Logging;
using Domain.Constants.Transaction;
using Domain.DTOs;
using Domain.DTOs.Logging;
using Domain.DTOs.Transaction;
using Domain.Events;
using Domain.Events.Payment;
using Domain.Exceptions;
using Domain.Models.Balance;
using Domain.Models.Transaction;
using Infrastructure.Services.Base;
using MongoDB.Driver;

namespace Infrastructure.Services.Transaction
{
    /// <summary>
    /// Enhanced transaction service with double-entry accounting support
    /// </summary>
    public class TransactionService : BaseService<TransactionData>, ITransactionService
    {
        private static readonly TimeSpan TRANSACTION_CACHE_DURATION = TimeSpan.FromMinutes(10);

        private readonly IAssetService _assetService;
        private readonly IBalanceService _balanceService;
        private readonly IPaymentService _paymentService;

        public TransactionService(
            IServiceProvider serviceProvider,
            IAssetService assetService,
            IBalanceService balanceService,
            IPaymentService paymentService
        ) : base(
            serviceProvider,
            new()
            {
                PublishCRUDEvents = true,
                IndexModels = [
                    new CreateIndexModel<TransactionData>(
                        Builders<TransactionData>.IndexKeys.Ascending(t => t.UserId),
                        new CreateIndexOptions { Name = "UserId_1" }),
                    new CreateIndexModel<TransactionData>(
                        Builders<TransactionData>.IndexKeys.Ascending(t => t.SubscriptionId),
                        new CreateIndexOptions { Name = "SubscriptionId_1" }),
                    new CreateIndexModel<TransactionData>(
                        Builders<TransactionData>.IndexKeys.Ascending(t => t.PaymentProviderId),
                        new CreateIndexOptions { Name = "PaymentProviderId_1" }),
                    // New indexes for double-entry queries
                    new CreateIndexModel<TransactionData>(
                        Builders<TransactionData>.IndexKeys.Ascending("FromBalance.UserId"),
                        new CreateIndexOptions { Name = "FromBalance_UserId_1" }),
                    new CreateIndexModel<TransactionData>(
                        Builders<TransactionData>.IndexKeys.Ascending("ToBalance.UserId"),
                        new CreateIndexOptions { Name = "ToBalance_UserId_1" }),
                    new CreateIndexModel<TransactionData>(
                        Builders<TransactionData>.IndexKeys.Ascending(t => t.IsConfirmed),
                        new CreateIndexOptions { Name = "IsConfirmed_1" }),
                    new CreateIndexModel<TransactionData>(
                        Builders<TransactionData>.IndexKeys.Ascending(t => t.IsReversed),
                        new CreateIndexOptions { Name = "IsReversed_1" })
                ]
            })
        {
            _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
            _balanceService = balanceService ?? throw new ArgumentNullException(nameof(balanceService));
            _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
        }

        /// <summary>
        /// Creates a transaction with double-entry accounting and balance updates
        /// This is the main method for creating transactions in the new system
        /// </summary>
        public async Task<ResultWrapper<TransactionData>> CreateTransactionAsync(
            TransactionData transaction,
            bool autoConfirm = true,
            CancellationToken cancellationToken = default)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Transaction",
                    FileName = "TransactionService",
                    OperationName = "CreateTransactionAsync",
                    State = {
                        ["UserId"] = transaction.UserId,
                        ["Action"] = transaction.Action,
                        ["AutoConfirm"] = autoConfirm
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    // Validate transaction structure
                    var (isValid, validationErrors) = transaction.Validate();
                    if (!isValid)
                    {
                        throw new ValidationException($"Invalid transaction", validationErrors ?? []);
                    }

                    // Start a database transaction for atomicity
                    return await ExecuteInTransactionAsync(async session =>
                    {
                        // Step 1: Insert the transaction record
                        var insertResult = await InsertAsync(transaction);
                        if (!insertResult.IsSuccess || insertResult.Data == null || !insertResult.Data.IsSuccess)
                        {
                            throw new DatabaseException($"Failed to insert transaction: {insertResult.ErrorMessage}");
                        }

                        var createdTransaction = insertResult.Data.Documents.First();

                        // Step 2: Process balance updates
                        await ProcessBalanceUpdatesAsync(createdTransaction, cancellationToken);

                        // Step 3: Auto-confirm if requested
                        if (autoConfirm && !createdTransaction.IsConfirmed)
                        {
                            createdTransaction.IsConfirmed = true;
                            createdTransaction.ConfirmedAt = DateTime.UtcNow;

                            var updateResult = await UpdateAsync(createdTransaction.Id, createdTransaction);
                            if (!updateResult.IsSuccess)
                            {
                                throw new DatabaseException($"Failed to confirm transaction: {updateResult.ErrorMessage}");
                            }
                        }

                        // Step 4: Invalidate affected user caches
                        var affectedUserIds = createdTransaction.GetAffectedUserIds();
                        await _balanceService.InvalidateBalanceCachesForUsersAsync(affectedUserIds);

                        _loggingService.LogInformation(
                            "Created transaction {TransactionId} for user {UserId} with action {Action}",
                            createdTransaction.Id, createdTransaction.UserId, createdTransaction.Action);

                        return createdTransaction;
                    }, cancellationToken);
                })
                .ExecuteAsync();
        }

        /// <summary>
        /// Processes balance updates for all entries in a transaction
        /// </summary>
        private async Task ProcessBalanceUpdatesAsync(
            TransactionData transaction,
            CancellationToken cancellationToken)
        {
            var updateTasks = new List<Task>();

            // Process FromBalance (debit)
            if (transaction.FromBalance != null)
            {
                updateTasks.Add(ApplyBalanceChangeAsync(
                    transaction.FromBalance,
                    transaction.Id,
                    cancellationToken));
            }

            // Process ToBalance (credit)
            if (transaction.ToBalance != null)
            {
                updateTasks.Add(ApplyBalanceChangeAsync(
                    transaction.ToBalance,
                    transaction.Id,
                    cancellationToken));
            }

            // Process Fee (typically a debit from user, credit to platform)
            if (transaction.Fee != null)
            {
                updateTasks.Add(ApplyBalanceChangeAsync(
                    transaction.Fee,
                    transaction.Id,
                    cancellationToken));
            }

            // Process Rounding adjustments
            if (transaction.Rounding != null)
            {
                updateTasks.Add(ApplyBalanceChangeAsync(
                    transaction.Rounding,
                    transaction.Id,
                    cancellationToken));
            }

            await Task.WhenAll(updateTasks);
        }

        /// <summary>
        /// Applies a single balance change with proper locking and validation
        /// </summary>
        private async Task ApplyBalanceChangeAsync(
            TransactionEntry entry,
            Guid transactionId,
            CancellationToken cancellationToken)
        {
            // Get or create the balance
            var filter = Builders<BalanceData>.Filter.And(
                Builders<BalanceData>.Filter.Eq(b => b.UserId, entry.UserId),
                Builders<BalanceData>.Filter.Eq(b => b.AssetId, entry.AssetId)
            );

            var balanceResult = await _balanceService.GetOneAsync(filter, cancellationToken);
            BalanceData balance;

            if (balanceResult == null || !balanceResult.IsSuccess || balanceResult.Data == null)
            {
                // Create new balance if it doesn't exist
                var asset = await _assetService.GetByIdAsync(entry.AssetId, cancellationToken);
                if (!asset.IsSuccess || asset.Data == null)
                {
                    throw new DatabaseException($"Asset {entry.AssetId} not found");
                }

                balance = new BalanceData
                {
                    UserId = entry.UserId,
                    AssetId = entry.AssetId,
                    Ticker = entry.Ticker ?? asset.Data.Ticker,
                    Available = 0,
                    Locked = 0,
                    Total = 0,
                    LastTransactionId = transactionId
                };
            }
            else
            {
                balance = balanceResult.Data;
            }

            // Record balance snapshot BEFORE the change
            entry.BalanceBeforeAvailable = balance.Available;
            entry.BalanceBeforeLocked = balance.Locked;
            entry.BalanceId = balance.Id;

            // Apply the balance change based on type
            switch (entry.BalanceType)
            {
                case BalanceType.Available:
                    balance.Available += entry.Quantity;
                    break;

                case BalanceType.Locked:
                    balance.Locked += entry.Quantity;
                    break;

                case BalanceType.LockFromAvailable:
                    balance.Available -= Math.Abs(entry.Quantity);
                    balance.Locked += Math.Abs(entry.Quantity);
                    break;

                case BalanceType.UnlockToAvailable:
                    balance.Locked -= Math.Abs(entry.Quantity);
                    balance.Available += Math.Abs(entry.Quantity);
                    break;
            }

            // Validate balance constraints
            if (balance.Available < 0)
            {
                throw new InsufficientBalanceException(
                    $"Insufficient available balance for user {entry.UserId}, asset {entry.AssetId}. " +
                    $"Required: {Math.Abs(entry.Quantity)}, Available: {entry.BalanceBeforeAvailable}");
            }

            if (balance.Locked < 0)
            {
                throw new InsufficientBalanceException(
                    $"Insufficient locked balance for user {entry.UserId}, asset {entry.AssetId}");
            }

            // Update total
            balance.Total = balance.Available + balance.Locked;
            balance.LastTransactionId = transactionId;
            balance.UpdatedAt = DateTime.UtcNow;

            // Record balance snapshot AFTER the change
            entry.BalanceAfterAvailable = balance.Available;
            entry.BalanceAfterLocked = balance.Locked;

            // Save the updated balance
            if (balance.Id == Guid.Empty)
            {
                var insertResult = await _balanceService.InsertAsync(balance);
                if (!insertResult.IsSuccess)
                {
                    throw new DatabaseException($"Failed to create balance: {insertResult.ErrorMessage}");
                }
            }
            else
            {
                var updateResult = await _balanceService.UpdateAsync(balance.Id, balance);
                if (!updateResult.IsSuccess)
                {
                    throw new DatabaseException($"Failed to update balance: {updateResult.ErrorMessage}");
                }
            }

            _loggingService.LogInformation(
                "Updated balance {BalanceId} for user {UserId}, asset {AssetId}: {Before} -> {After}",
                balance.Id, entry.UserId, entry.AssetId,
                entry.BalanceBeforeAvailable, entry.BalanceAfterAvailable);
        }

        /// <summary>
        /// Reverses a transaction by creating a compensating transaction
        /// </summary>
        public async Task<ResultWrapper<TransactionData>> ReverseTransactionAsync(
            Guid transactionId,
            string reason,
            CancellationToken cancellationToken = default)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Transaction",
                    FileName = "TransactionService",
                    OperationName = "ReverseTransactionAsync",
                    State = {
                        ["TransactionId"] = transactionId,
                        ["Reason"] = reason
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    // Get the original transaction
                    var originalResult = await GetByIdAsync(transactionId);
                    if (!originalResult.IsSuccess || originalResult.Data == null)
                    {
                        throw new KeyNotFoundException($"Transaction {transactionId} not found");
                    }

                    var originalTransaction = originalResult.Data;

                    // Check if already reversed
                    if (originalTransaction.IsReversed)
                    {
                        throw new InvalidOperationException(
                            $"Transaction {transactionId} has already been reversed");
                    }

                    // Create reversal transaction
                    var reversalTransaction = CommonTransactions
                        .Refund(originalTransaction, reason)
                        .Build();

                    // Create the reversal
                    var reversalResult = await CreateTransactionAsync(
                        reversalTransaction,
                        autoConfirm: true,
                        cancellationToken: cancellationToken);

                    if (!reversalResult.IsSuccess || reversalResult.Data == null)
                    {
                        throw new DatabaseException($"Failed to create reversal: {reversalResult.ErrorMessage}");
                    }

                    // Mark original as reversed
                    originalTransaction.IsReversed = true;
                    originalTransaction.ReversedByTransactionId = reversalResult.Data.Id;
                    originalTransaction.UpdatedAt = DateTime.UtcNow;

                    var updateResult = await UpdateAsync(originalTransaction.Id, originalTransaction);
                    if (!updateResult.IsSuccess || !updateResult.Data.IsSuccess)
                    {
                        throw new DatabaseException($"Failed to mark transaction as reversed: {updateResult.ErrorMessage}");
                    }

                    _loggingService.LogInformation(
                        "Reversed transaction {OriginalId} with reversal {ReversalId}",
                        transactionId, reversalResult.Data.Id);

                    return reversalResult.Data;
                })
                .ExecuteAsync();
        }

        /// <summary>
        /// Gets all transactions affecting a specific user (as sender or receiver)
        /// </summary>
        public async Task<ResultWrapper<PaginatedResult<TransactionData>>> GetUserTransactionsAsync(
            Guid userId,
            int page = 1,
            int pageSize = 20,
            bool includeAsReceiver = true)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Transaction",
                    FileName = "TransactionService",
                    OperationName = "GetUserTransactionsAsync",
                    State = {
                        ["UserId"] = userId,
                        ["Page"] = page,
                        ["PageSize"] = pageSize,
                        ["IncludeAsReceiver"] = includeAsReceiver
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    FilterDefinition<TransactionData> filter;

                    if (includeAsReceiver)
                    {
                        // Get transactions where user is primary user OR in FromBalance OR in ToBalance
                        filter = Builders<TransactionData>.Filter.Or(
                            Builders<TransactionData>.Filter.Eq(t => t.UserId, userId),
                            Builders<TransactionData>.Filter.Eq("FromBalance.UserId", userId),
                            Builders<TransactionData>.Filter.Eq("ToBalance.UserId", userId)
                        );
                    }
                    else
                    {
                        // Only get transactions where user is the primary user
                        filter = Builders<TransactionData>.Filter.Eq(t => t.UserId, userId);
                    }

                    var sort = Builders<TransactionData>.Sort.Descending(t => t.CreatedAt);

                    var result = await GetPaginatedAsync(filter, sort, page, pageSize);

                    if (result == null || !result.IsSuccess || result.Data == null)
                    {
                        throw new TransactionFetchException($"Failed to fetch user {userId} transactions: {result?.ErrorMessage}");
                    }

                    return result.Data;
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<List<TransactionData>>> GetSubscriptionTransactionsAsync(Guid subscriptionId)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Transaction",
                    FileName = "TransactionService",
                    OperationName = "GetSubscriptionTransactionsAsync",
                    State = {
                        ["SubscriptionId"] = subscriptionId,
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    FilterDefinition<TransactionData> filter;

                    filter = Builders<TransactionData>.Filter.And(
                        Builders<TransactionData>.Filter.Eq(t => t.SubscriptionId, subscriptionId),
                        Builders<TransactionData>.Filter.In("Action", [TransactionActionType.Buy, TransactionActionType.Sell])
                    );

                    var result = await GetManyAsync(filter);

                    if (result == null || !result.IsSuccess || result.Data == null)
                    {
                        throw new TransactionFetchException($"Failed to fetch subscription {subscriptionId} transactions: {result?.ErrorMessage}");
                    }

                    return result.Data;
                })
                .ExecuteAsync();
        }

        /// <summary>
        /// Gets the balance history for a user's specific asset
        /// Useful for showing how balance changed over time
        /// </summary>
        public async Task<ResultWrapper<List<TransactionData>>> GetBalanceHistoryAsync(
            Guid userId,
            Guid assetId,
            DateTime? fromDate = null,
            DateTime? toDate = null)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Transaction",
                    FileName = "TransactionService",
                    OperationName = "GetBalanceHistoryAsync",
                    State = {
                        ["UserId"] = userId,
                        ["AssetId"] = assetId,
                        ["FromDate"] = fromDate,
                        ["ToDate"] = toDate
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    var filters = new List<FilterDefinition<TransactionData>>
                    {
                        Builders<TransactionData>.Filter.Or(
                            Builders<TransactionData>.Filter.And(
                                Builders<TransactionData>.Filter.Eq("FromBalance.UserId", userId),
                                Builders<TransactionData>.Filter.Eq("FromBalance.AssetId", assetId)
                            ),
                            Builders<TransactionData>.Filter.And(
                                Builders<TransactionData>.Filter.Eq("ToBalance.UserId", userId),
                                Builders<TransactionData>.Filter.Eq("ToBalance.AssetId", assetId)
                            )
                        )
                    };

                    if (fromDate.HasValue)
                    {
                        filters.Add(Builders<TransactionData>.Filter.Gte(t => t.CreatedAt, fromDate.Value));
                    }

                    if (toDate.HasValue)
                    {
                        filters.Add(Builders<TransactionData>.Filter.Lte(t => t.CreatedAt, toDate.Value));
                    }

                    var filter = Builders<TransactionData>.Filter.And(filters);
                    var sort = Builders<TransactionData>.Sort.Ascending(t => t.CreatedAt);
                    var result = await GetManySortedAsync(filter, sort);

                    if (!result.IsSuccess)
                    {
                        throw new TransactionFetchException($"Failed to fetch balance history: {result.ErrorMessage}");
                    }

                    return result.Data?.ToList() ?? new List<TransactionData>();
                })
                .ExecuteAsync();
        }

        // ===== Event Handlers (Updated for new model) =====

        public async Task Handle(PaymentReceivedEvent notification, CancellationToken cancellationToken)
        {
            var payment = notification.Payment;
            await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services",
                    FileName = "TransactionService",
                    OperationName = "Handle(PaymentReceivedEvent notification, CancellationToken cancellationToken)",
                    State = {
                        ["PaymentId"] = payment.Id,
                        ["UserId"] = payment.UserId,
                        ["Amount"] = payment.TotalAmount.ToString(),
                        ["Currency"] = payment.Currency,
                    },
                    LogLevel = LogLevel.Critical
                },
                async () =>
                {                    
                    var enhancedBalanceResult = await _balanceService.GetOrCreateEnhancedBalanceAsync(payment.UserId, payment.Currency.ToUpperInvariant() ?? "USD");

                    if (!enhancedBalanceResult.IsSuccess)
                    {
                        throw new BalanceFetchException($"Failed to fetch user balance for currency {payment.Currency}");
                    }

                    var enhancedBalance = enhancedBalanceResult.Data;


                    // Create external deposit transaction
                    var transaction = CommonTransactions
                        .ExternalDeposit(
                            userId: payment.UserId,
                            assetId: enhancedBalance.AssetId,
                            balanceId: enhancedBalanceResult.Data.Id,
                            ticker: enhancedBalance.Ticker,
                            amount: payment.NetAmount,
                            sourceName: payment.Provider,
                            sourceId: payment.PaymentProviderId,
                            paymentProviderId: payment.PaymentProviderId)
                        .WithSubscription(payment.SubscriptionId)
                        .Build();

                    var result = await CreateTransactionAsync(transaction, autoConfirm: true, cancellationToken);

                    if (!result.IsSuccess)
                    {
                        throw new DatabaseException($"Failed to create deposit transaction: {result.ErrorMessage}");
                    }

                    _loggingService.LogInformation(
                        "Created deposit transaction {TransactionId} for payment {PaymentId}",
                        result.Data?.Id, payment.Id);
                })
                .ExecuteAsync();
        }

        public async Task Handle(ExchangeOrderCompletedEvent notification, CancellationToken cancellationToken)
        {
            var order = notification.Order;
            await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services",
                    FileName = "TransactionService",
                    OperationName = "Handle(ExchangeOrderCompletedEvent)",
                    State = {
                        ["OrderId"] = order.Id,
                        ["Exchange"] = order.Exchange,
                        ["Ticker"] = order.Ticker,
                        ["Quantity"] = order.Quantity.ToString(),
                    },
                    LogLevel = LogLevel.Critical
                },
                async () =>
                {
                    // Get payment details
                    var paymentResult = await _paymentService.GetByProviderIdAsync(order.PaymentProviderId);
                    if (!paymentResult.IsSuccess || paymentResult.Data == null)
                    {
                        throw new DatabaseException($"Payment {order.PaymentProviderId} not found");
                    }

                    var payment = paymentResult.Data;

                    // Get quote asset (typically USD or stable coin)
                    var quoteBalanceTask = _balanceService.GetOrCreateEnhancedBalanceAsync(payment.UserId, payment.Currency.ToUpperInvariant());
                    var cryptoBalanceTask = _balanceService.GetOrCreateEnhancedBalanceAsync(payment.UserId, order.Ticker.ToUpperInvariant());

                    await Task.WhenAll(quoteBalanceTask, cryptoBalanceTask);

                    var quoteBalanceResult = quoteBalanceTask.Result;
                    if(quoteBalanceResult == null || !quoteBalanceResult.IsSuccess ||  quoteBalanceResult.Data == null)
                    {
                        throw new BalanceFetchException(quoteBalanceResult.ErrorMessage);
                    }

                    var cryptoBalanceResult = cryptoBalanceTask.Result;
                    if (cryptoBalanceResult == null || !cryptoBalanceResult.IsSuccess || cryptoBalanceResult.Data == null)
                    {
                        throw new BalanceFetchException(cryptoBalanceResult.ErrorMessage);
                    }

                    var quoteBalance = quoteBalanceResult.Data;
                    var cryptoBalance = cryptoBalanceResult.Data;

                    // Create crypto purchase transaction
                    var transaction = CommonTransactions
                        .CryptoPurchase(
                            userId: order.UserId,
                            fiatBalanceId: quoteBalance.Id,
                            fiatAssetId: quoteBalance.AssetId,
                            fiatTicker: quoteBalance.Ticker.ToUpperInvariant(),
                            fiatAmount: order.QuoteQuantityFilled ?? 0,
                            cryptoBalanceId: cryptoBalance.Id,
                            cryptoAssetId: order.AssetId,
                            cryptoTicker: order.Ticker.ToUpperInvariant(),
                            cryptoAmount: order.Quantity ?? 0,
                            exchange: order.Exchange,
                            orderId: order.PlacedOrderId?.ToString() ?? order.Id.ToString())
                        .WithPaymentProvider(order.PaymentProviderId)
                        .WithSubscription(order.SubscriptionId)
                        .Build();

                    var result = await CreateTransactionAsync(transaction, autoConfirm: true, cancellationToken);

                    if (!result.IsSuccess)
                    {
                        throw new DatabaseException($"Failed to create exchange transaction: {result.ErrorMessage}");
                    }

                    _loggingService.LogInformation(
                        "Created exchange transaction {TransactionId} for order {OrderId}",
                        result.Data?.Id, order.Id);
                })
                .ExecuteAsync();
        }

        public async Task Handle(WithdrawalApprovedEvent notification, CancellationToken cancellationToken)
        {
            await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services",
                    FileName = "TransactionService",
                    OperationName = "Handle(WithdrawalApprovedEvent)",
                    State = {
                        ["WithdrawalId"] = notification.Withdrawal.Id,
                        ["UserId"] = notification.Withdrawal.UserId,
                        ["Amount"] = notification.Withdrawal.Amount.ToString(),
                        ["Currency"] = notification.Withdrawal.Currency,
                    },
                    LogLevel = LogLevel.Critical
                },
                async () =>
                {
                    var withdrawal = notification.Withdrawal;

                    var balanceResult = await _balanceService.GetOrCreateEnhancedBalanceAsync(withdrawal.UserId, withdrawal.Currency.ToUpperInvariant());
                    if (balanceResult == null || !balanceResult.IsSuccess)
                    {
                        throw new BalanceFetchException(balanceResult.ErrorMessage);
                    }

                    var balance = balanceResult.Data;

                    // Create external withdrawal transaction
                    var transaction = CommonTransactions
                        .ExternalWithdrawal(
                            userId: withdrawal.UserId,
                            balanceId: balance.Id,
                            assetId: balance.AssetId,
                            ticker: balance.Ticker.ToUpperInvariant(),
                            amount: withdrawal.Amount,
                            destination: withdrawal.WithdrawalMethod ?? "External",
                            transactionHash: withdrawal.TransactionHash ?? withdrawal.Id.ToString())
                        .Build();

                    var result = await CreateTransactionAsync(transaction, autoConfirm: true, cancellationToken);

                    if (!result.IsSuccess)
                    {
                        throw new DatabaseException($"Failed to create withdrawal transaction: {result.ErrorMessage}");
                    }

                    _loggingService.LogInformation(
                        "Created withdrawal transaction {TransactionId} for withdrawal {WithdrawalId}",
                        result.Data?.Id, withdrawal.Id);
                })
                .ExecuteAsync();
        }
    }
}