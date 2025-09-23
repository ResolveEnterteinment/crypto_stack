using Application.Interfaces;
using Application.Interfaces.Asset;
using Application.Interfaces.Payment;
using Application.Interfaces.Subscription;
using Domain.Constants.Logging;
using Domain.Constants.Transaction;
using Domain.DTOs;
using Domain.DTOs.Logging;
using Domain.DTOs.Subscription;
using Domain.Events;
using Domain.Events.Payment;
using Domain.Exceptions;
using Domain.Models.Transaction;
using Infrastructure.Services.Base;
using MongoDB.Driver;

namespace Infrastructure.Services.Transaction
{
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
                        new CreateIndexOptions { Name = "PaymentProviderId_1" })
                    ]
            })
        {
            _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
            _balanceService = balanceService ?? throw new ArgumentNullException(nameof(balanceService));
            _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
        }

        public async Task<ResultWrapper<PaginatedResult<TransactionData>>> GetUserTransactionsAsync(Guid userId, int page = 1, int pageSize = 20)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Transaction",
                    FileName = "TransactionService",
                    OperationName = "GetUserTransactionsAsync(Guid userId, int page = 1, int pageSize = 20)",
                    State =
                    {
                        ["UserId"] = userId,
                        ["Page"] = page,
                        ["PageSize"] = pageSize
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    var filter = Builders<TransactionData>.Filter.Eq(t => t.UserId, userId);
                    var result = await GetPaginatedAsync(filter, page, pageSize, sortField: "CreatedAt", false);
                    if (result == null || !result.IsSuccess || result.Data == null)
                        throw new TransactionFetchException($"Failed to fetch user {userId} transactions: {result.ErrorMessage}");
                    var transactions = result.Data;
                    return transactions;
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<List<TransactionDto>>> GetBySubscriptionIdAsync(Guid subscriptionId)
        {
            var result = await _resilienceService.SafeExecute(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Transaction",
                    FileName = "TransactionService",
                    OperationName = "GetBySubscriptionIdAsync(Guid subscriptionId)",
                    State = {
                        ["SubscriptionId"] = subscriptionId,
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    var filter = Builders<TransactionData>.Filter.Eq(t => t.SubscriptionId, subscriptionId);

                    var dataResult = await GetManyAsync(filter);

                    if (dataResult == null || !dataResult.IsSuccess)
                        throw new KeyNotFoundException($"Failed to fetch subscripiton transactions");

                    var transactionsDto = new List<TransactionDto>();

                    foreach (var txn in dataResult.Data)
                    {
                        var assetWr = await _assetService.GetByIdAsync(txn.AssetId);
                        if (assetWr == null || !assetWr.IsSuccess || assetWr.Data == null)
                        {
                            _loggingService.LogError("Skipping transaction {TxnId} due to missing asset data", txn.Id);
                            continue;
                        }

                        var paymentWr = await _paymentService.GetByProviderIdAsync(txn.PaymentProviderId);
                        if (!paymentWr.IsSuccess || paymentWr.Data == null)
                        {
                            _loggingService.LogError("Skipping transaction {TxnId} due to missing payment data", txn.Id);
                            continue;
                        }
                        
                        transactionsDto.Add(new TransactionDto
                        {
                            Action = txn.Action,
                            AssetName = assetWr.Data.Name,
                            AssetTicker = assetWr.Data.Ticker,
                            Quantity = txn.Quantity,
                            CreatedAt = txn.CreatedAt,
                            QuoteQuantity = paymentWr.Data.NetAmount,
                            QuoteCurrency = paymentWr.Data.Currency
                        });
                    }
                    return transactionsDto;
                }
            );
            if (result == null || !result.IsSuccess)
            {
                await _loggingService.LogTraceAsync($"Failed to get transactions for subscriptions ID {subscriptionId}: {result?.ErrorMessage ?? "Fetch result returned null"}");
            }

            return result;
        }

        public async Task<ResultWrapper<List<TransactionDto>>> GetByUserAsset(Guid userId, Guid assetId)
        {
            return await _resilienceService.SafeExecute(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Transaction",
                    FileName = "TransactionService",
                    OperationName = "GetByUserBalance(string userId, string assetId)",
                    State = {
                        ["UserId"] = userId,
                        ["AssetId"] = assetId,
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    var filter = Builders<TransactionData>.Filter.And(
                        [Builders<TransactionData>.Filter.Eq(t => t.UserId, userId),
                        Builders<TransactionData>.Filter.Eq(t => t.AssetId, assetId)]
                    );

                    var dataResult = await GetManyAsync(filter);

                    if (dataResult == null || !dataResult.IsSuccess)
                        throw new DatabaseException($"Failed to fetch user transactions");

                    var transactionsDto = new List<TransactionDto>();

                    foreach (var txn in dataResult.Data)
                    {
                        var assetWr = await _assetService.GetByIdAsync(txn.AssetId);
                        if (assetWr == null || !assetWr.IsSuccess || assetWr.Data == null)
                        {
                            _loggingService.LogError("Skipping transaction {TxnId} due to missing asset data", txn.Id);
                            continue;
                        }
                        
                        var paymentWr = await _paymentService.GetByProviderIdAsync(txn.PaymentProviderId);
                        
                        if (!paymentWr.IsSuccess || paymentWr.Data == null)
                        {
                            _loggingService.LogError("Skipping transaction {TxnId} due to missing balance or payment data", txn.Id);
                            continue;
                        }

                        transactionsDto.Add(new TransactionDto
                        {
                            Action = txn.Action,
                            AssetName = assetWr.Data.Name,
                            AssetTicker = assetWr.Data.Ticker,
                            Quantity = txn.Quantity,
                            CreatedAt = txn.CreatedAt,
                            QuoteQuantity = paymentWr.Data.NetAmount,
                            QuoteCurrency = paymentWr.Data.Currency
                        });
                    }
                    return transactionsDto;
                }
            );
        }

        public async Task Handle(PaymentReceivedEvent notification, CancellationToken cancellationToken)
        {
            await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services",
                    FileName = "TransactionService",
                    OperationName = "Handle(PaymentReceivedEvent notification, CancellationToken cancellationToken)",
                    State = {
                        ["Notification"] = notification.Payment.Id,
                        ["Amount"] = notification.Payment.TotalAmount,
                        ["Currency"] = notification.Payment.Currency,
                        ["CancellationToken"] = cancellationToken,
                    },
                    LogLevel = LogLevel.Critical
                },
                async () =>
                {
                    var payment = notification.Payment;

                    // Fetch asset by currency ticker
                    var assetResult = await _assetService.GetByTickerAsync(payment.Currency);

                    if(assetResult == null || !assetResult.IsSuccess)
                    {
                        throw new DatabaseException($"Asset not found for currency {payment.Currency}");
                    }

                    var transaction = new TransactionData
                    {
                        UserId = payment.UserId,
                        PaymentProviderId = payment.PaymentProviderId,
                        SubscriptionId = payment.SubscriptionId,
                        AssetId = assetResult.Data.Id,
                        SourceName = payment.Provider,
                        SourceId = payment.PaymentProviderId,
                        Action = TransactionActionType.Deposit,
                        Quantity = payment.NetAmount,
                        Description = $"Payment received for user {payment.UserId} with amount {payment.TotalAmount} {payment.Currency}"
                    };

                    var insertResult = await InsertAsync(transaction);

                    if (insertResult == null || !insertResult.IsSuccess)
                    {
                        throw new DatabaseException($"Failed to create transaction for user {payment.UserId}: {insertResult?.ErrorMessage ?? "Upsert result returned null"}");
                    }
                })
                .ExecuteAsync();
        }

        public async Task Handle(ExchangeOrderCompletedEvent notification, CancellationToken cancellationToken)
        {
            await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services",
                    FileName = "TransactionService",
                    OperationName = "Handle(ExchangeOrderCompletedEvent notification, CancellationToken cancellationToken)",
                    State = {
                        ["OrderId"] = notification.Order.Id,
                        ["Exchange"] = notification.Order.Exchange,
                        ["Ticker"] = notification.Order.Ticker,
                        ["Quantity"] = notification.Order.Quantity.ToString(),
                    },
                    LogLevel = LogLevel.Critical
                },
                async () =>
                {
                    var order = notification.Order;

                    var paymentResult = await _paymentService.GetByProviderIdAsync(order.PaymentProviderId);

                    if(paymentResult == null || !paymentResult.IsSuccess)
                    {
                        throw new DatabaseException($"Failed to fetch payment ID {order.PaymentProviderId}: {paymentResult?.ErrorMessage ?? "Fetch result returned null"}");
                    }

                    var quoteAssetResult = await _assetService.GetByTickerAsync(paymentResult.Data.Currency ?? "USD");

                    if (quoteAssetResult == null || !quoteAssetResult.IsSuccess)
                    {
                        throw new DatabaseException($"Failed to fetch balance for user {order.UserId} and currency {order.Ticker}: {quoteAssetResult?.ErrorMessage ?? "Fetch result returned null"}");
                    }

                    var quoteTransaction = new TransactionData
                    {
                        UserId = order.UserId,
                        PaymentProviderId = order.PaymentProviderId,
                        SubscriptionId = order.SubscriptionId,
                        AssetId = quoteAssetResult.Data.Id,
                        SourceName = order.Exchange,
                        SourceId = order.PlacedOrderId.ToString(),
                        Action = order.Side == TransactionActionType.Sell.ToUpperInvariant() ? TransactionActionType.Buy : TransactionActionType.Sell,
                        Quantity = -order.QuoteQuantityFilled.Value,
                        Description = $"{order.Exchange} exchange {order.Side} order ID {order.PlacedOrderId.ToString()}"
                    };

                    var assetTransaction = new TransactionData
                    {
                        UserId = order.UserId,
                        PaymentProviderId = order.PaymentProviderId,
                        SubscriptionId = order.SubscriptionId,
                        AssetId = order.AssetId,
                        SourceName = order.Exchange,
                        SourceId = order.PlacedOrderId.ToString(),
                        Action = TransactionActionType.Buy,
                        Quantity = order.Quantity.Value,
                        Description = $"{order.Exchange} exchange {order.Side} order ID {order.PlacedOrderId.ToString()}"
                    };

                    var insertQuote = InsertAsync(quoteTransaction);
                    var insertAsset = InsertAsync(assetTransaction);

                    var result = await ExecuteInTransactionAsync(async session =>
                    {
                        await insertQuote.ConfigureAwait(false);
                        await insertAsset.ConfigureAwait(false);

                        return ResultWrapper.Success();
                    }, cancellationToken);

                    if (result == null || !result.IsSuccess)
                    {
                        throw new DatabaseException($"Failed to upsert balance for user {order.UserId}: {result?.ErrorMessage ?? "Upsert result returned null"}");
                    }
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
                    OperationName = "Handle(WithdrawalApprovedEvent notification, CancellationToken cancellationToken)",
                    State = {
                        ["EventId"] = notification.EventId,
                        ["WithdrawalId"] = notification.Withdrawal.Id,
                        ["Currency"] = notification.Withdrawal.Currency,
                        ["Amount"] = notification.Withdrawal.RequestedBy,
                        ["WithdrawalMethod"] = notification.Withdrawal.WithdrawalMethod,
                        ["TransactionHash"] = notification.Withdrawal.TransactionHash,
                    },
                    LogLevel = LogLevel.Critical
                },
                async () =>
                {
                    var withdrawal = notification.Withdrawal;

                    // Fetch asset by currency ticker
                    var assetResult = await _assetService.GetByTickerAsync(withdrawal.Currency);

                    if (assetResult == null || !assetResult.IsSuccess)
                    {
                        throw new DatabaseException($"Asset not found for currency {withdrawal.Currency}");
                    }

                    var transaction = new TransactionData
                    {
                        UserId = withdrawal.UserId,
                        AssetId = assetResult.Data.Id,
                        SourceName = assetResult.Data.Exchange,
                        SourceId = withdrawal.Id.ToString(),
                        Action = TransactionActionType.Withdrawal,
                        Quantity = withdrawal.Amount,
                        Description = $"User withdrawal of {withdrawal.Amount} {withdrawal.Currency}"
                    };

                    var insertResult = await InsertAsync(transaction);

                    if (insertResult == null || !insertResult.IsSuccess)
                    {
                        throw new DatabaseException($"Failed to upsert balance for user {withdrawal.UserId}: {insertResult?.ErrorMessage ?? "Upsert result returned null"}");
                    }
                })
                .ExecuteAsync();
        }

    }
}
