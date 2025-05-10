using Application.Interfaces;
using Application.Interfaces.Asset;
using Application.Interfaces.Base;
using Application.Interfaces.Logging;
using Application.Interfaces.Payment;
using Application.Interfaces.Subscription;
using Domain.DTOs;
using Domain.DTOs.Subscription;
using Domain.Exceptions;
using Domain.Models.Transaction;
using Infrastructure.Services.Base;
using MongoDB.Driver;

namespace Infrastructure.Services
{
    public class TransactionService : BaseService<TransactionData>, ITransactionService
    {
        private static readonly TimeSpan TRANSACTION_CACHE_DURATION = TimeSpan.FromMinutes(10);

        private const string CACHE_KEY_USER_TRANSACTIONS = "user_transactions:{0}";
        private const string CACHE_KEY_SUBSCRIPTION_TRANSACTIONS = "subscription_transactions:{0}";

        private readonly ISubscriptionService _subscriptionService;
        private readonly IAssetService _assetService;
        private readonly IBalanceService _balanceService;
        private readonly IPaymentService _paymentService;

        public TransactionService(
            ICrudRepository<TransactionData> repository,
            ICacheService<TransactionData> cacheService,
            IMongoIndexService<TransactionData> indexService,
            ILoggingService logger,
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
            using var scope = Logger.BeginScope("TransactionService::GetUserTransactionsAsync", new
            {
                UserId = userId,
            });

            try
            {
                var filter = Builders<TransactionData>.Filter.Eq(t => t.UserId, userId);
                var result = await GetPaginatedAsync(filter, page, pageSize, sortField: "CreatedAt", false);
                if (result == null || !result.IsSuccess || result.Data == null)
                    throw new TransactionFetchException($"Failed to fetch user {userId} transactions: {result.ErrorMessage}");
                var transactions = result.Data;
                return ResultWrapper<PaginatedResult<TransactionData>>.Success(transactions);
            }
            catch (Exception ex)
            {
                await Logger.LogTraceAsync(ex.Message);
                return ResultWrapper<PaginatedResult<TransactionData>>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<List<TransactionDto>>> GetBySubscriptionIdAsync(Guid subscriptionId)
        {
            using var scope = Logger.BeginScope("TransactionService::GetUserTransactionsAsync", new
            {
                SubscriptionId = subscriptionId,
            });

            var result = await FetchCached(
                string.Format(CACHE_KEY_SUBSCRIPTION_TRANSACTIONS, subscriptionId),
                async () =>
                {
                    var filter = Builders<TransactionData>.Filter.Eq(t => t.SubscriptionId, subscriptionId);
                    var dataResult = await _repository.GetAllAsync(filter);
                    if (dataResult == null)
                        throw new KeyNotFoundException($"Failed to fetch subscripiton transactions");

                    var transactionsDto = new List<TransactionDto>();
                    foreach (var txn in dataResult!)
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
            if (result == null || !result.IsSuccess)
            {
                await Logger.LogTraceAsync($"Failed to get transactions for subscriptions ID {subscriptionId}: {result?.ErrorMessage ?? "Fetch result returned null"}");
            }

            return result;
        }

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
                Logger.LogError("Failed to get transactions for provider {ProviderId}", paymentProviderId);
                return ResultWrapper<IEnumerable<TransactionData>>.FromException(ex);
            }
        }

        public async Task<ResultWrapper> CreateTransactionAsync(TransactionData transaction)
        {
            using var scope = Logger.BeginScope("TransactionService::CreateTransactionAsync", new Dictionary<string, object?>
            {
                ["Transaction"] = transaction,
            });

            try
            {
                if (transaction == null)
                    throw new ArgumentNullException(nameof(transaction));

                if (transaction.Id == Guid.Empty)
                    transaction.Id = Guid.NewGuid();

                var insertResult = await InsertAsync(transaction);
                if (insertResult == null || !insertResult.IsSuccess || !insertResult.Data.IsSuccess)
                    throw new DatabaseException("Failed to create transaction record");

                return ResultWrapper.Success();
            }
            catch (Exception ex)
            {
                await Logger.LogTraceAsync("Failed to create transaction: {ErrorMessage}", ex.Message);
                return ResultWrapper.FromException(ex);
            }
        }
    }
}
