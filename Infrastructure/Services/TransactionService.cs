using Application.Interfaces;
using Application.Interfaces.Asset;
using Application.Interfaces.Base;
using Application.Interfaces.Payment;
using Application.Interfaces.Subscription;
using Domain.DTOs;
using Domain.DTOs.Subscription;
using Domain.Models.Transaction;
using Infrastructure.Services.Base;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Infrastructure.Services
{
    public class TransactionService : BaseService<TransactionData>, ITransactionService
    {
        private static readonly TimeSpan TRANSACTION_CACHE_DURATION = TimeSpan.FromMinutes(10);
        private readonly ISubscriptionService _subscriptionService;
        private readonly IAssetService _assetService;
        private readonly IBalanceService _balanceService;
        private readonly IPaymentService _paymentService;

        public TransactionService(
            ICrudRepository<TransactionData> repository,
            ICacheService<TransactionData> cacheService,
            IMongoIndexService<TransactionData> indexService,
            ILogger<TransactionService> logger,
            IEventService eventService,
            ISubscriptionService subscriptionService,
            IAssetService assetService,
            IBalanceService balanceService,
            IPaymentService paymentService
        ) : base(
            repository,
            cacheService,
            indexService,
            logger,
            eventService,
            new[]
            {
                new CreateIndexModel<TransactionData>(
                    Builders<TransactionData>.IndexKeys.Ascending(t => t.UserId),
                    new CreateIndexOptions { Name = "UserId_1" }),
                new CreateIndexModel<TransactionData>(
                    Builders<TransactionData>.IndexKeys.Ascending(t => t.SubscriptionId),
                    new CreateIndexOptions { Name = "SubscriptionId_1" }),
                new CreateIndexModel<TransactionData>(
                    Builders<TransactionData>.IndexKeys.Ascending(t => t.PaymentProviderId),
                    new CreateIndexOptions { Name = "PaymentProviderId_1" })
            }
        )
        {
            _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
            _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
            _balanceService = balanceService ?? throw new ArgumentNullException(nameof(balanceService));
            _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
        }

        public async Task<ResultWrapper<PaginatedResult<TransactionData>>> GetUserTransactionsAsync(Guid userId, int page = 1, int pageSize = 20)
        {
            var filter = Builders<TransactionData>.Filter.Eq(t => t.UserId, userId);
            return await GetPaginatedAsync(filter, page, pageSize, sortField: "CreatedAt", false);
        }

        public async Task<ResultWrapper<List<TransactionDto>>> GetBySubscriptionIdAsync(Guid subscriptionId)
            => await FetchCached(
                $"subscription:transactions:{subscriptionId}",
                async () =>
                {
                    var filter = Builders<TransactionData>.Filter.Eq(t => t.SubscriptionId, subscriptionId);
                    var dataResult = await GetManyAsync(filter);
                    if (dataResult == null || !dataResult.IsSuccess)
                        throw new KeyNotFoundException(dataResult?.ErrorMessage ?? $"Subscripiton transactions fetch returned null");

                    var transactionsDto = new List<TransactionDto>();
                    foreach (var txn in dataResult.Data!)
                    {
                        var balanceWr = await _balanceService.GetByIdAsync(txn.BalanceId);
                        var paymentWr = await _paymentService.GetByProviderIdAsync(txn.PaymentProviderId);
                        if (!balanceWr.IsSuccess || balanceWr.Data == null || !paymentWr.IsSuccess || paymentWr.Data == null)
                        {
                            Logger.LogError("Skipping transaction {TxnId} due to missing balance or payment data", txn.Id);
                            continue;
                        }

                        var assetWr = await _assetService.GetByIdAsync(balanceWr.Data.AssetId);
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
                },
                TRANSACTION_CACHE_DURATION
            );

        public async Task<ResultWrapper<IEnumerable<TransactionData>>> GetByPaymentProviderIdAsync(string paymentProviderId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(paymentProviderId))
                    throw new ArgumentException("PaymentProviderId cannot be null or empty", nameof(paymentProviderId));

                var filter = Builders<TransactionData>.Filter.Eq(t => t.PaymentProviderId, paymentProviderId);
                var dataResult = await GetManyAsync(filter);
                if (dataResult == null || !dataResult.IsSuccess)
                    throw new KeyNotFoundException(dataResult?.ErrorMessage ?? "Transaction fetch returned null");

                return ResultWrapper<IEnumerable<TransactionData>>.Success(dataResult.Data!);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to get transactions for provider {ProviderId}", paymentProviderId);
                return ResultWrapper<IEnumerable<TransactionData>>.FromException(ex);
            }
        }

        public async Task<ResultWrapper> CreateTransactionAsync(TransactionData transaction)
        {
            try
            {
                if (transaction == null)
                    throw new ArgumentNullException(nameof(transaction));

                if (transaction.Id == Guid.Empty)
                    transaction.Id = Guid.NewGuid();

                var insertResult = await InsertAsync(transaction);
                if (!insertResult.IsSuccess)
                    return ResultWrapper.Failure(Domain.Constants.FailureReason.DatabaseError,
                        "Failed to create transaction record"
                    );

                return ResultWrapper.Success();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to create transaction");
                return ResultWrapper.FromException(ex);
            }
        }
    }
}
