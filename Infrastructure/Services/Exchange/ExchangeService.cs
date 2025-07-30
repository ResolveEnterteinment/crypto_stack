using Application.Interfaces;
using Application.Interfaces.Asset;
using Application.Interfaces.Base;
using Application.Interfaces.Exchange;
using Application.Interfaces.Logging;
using Application.Interfaces.Subscription;
using Binan‌​ceLibrary;
using Domain.Constants;
using Domain.DTOs;
using Domain.DTOs.Exchange;
using Domain.Exceptions;
using Domain.Models.Exchange;
using Infrastructure.Services.Base;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Polly;
using Polly.Retry;

namespace Infrastructure.Services.Exchange
{
    public class ExchangeService : BaseService<ExchangeOrderData>, IExchangeService
    {
        private readonly Dictionary<string, IExchange> _exchanges = new(StringComparer.OrdinalIgnoreCase);
        private readonly IOptions<ExchangeServiceSettings> _settings;
        private readonly AsyncRetryPolicy _retryPolicy;
        private readonly IAssetService _assetService;

        private static readonly TimeSpan PriceCacheDuration = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan BalanceCacheDuration = TimeSpan.FromMinutes(2);

        public IReadOnlyDictionary<string, IExchange> Exchanges => _exchanges;
        public IExchange DefaultExchange => _exchanges.Values.FirstOrDefault()
            ?? throw new InvalidOperationException("No exchanges configured");
        public Guid FiatAssetId { get; }

        public ExchangeService(
            ICrudRepository<ExchangeOrderData> repository,
            ICacheService<ExchangeOrderData> cacheService,
            IMongoIndexService<ExchangeOrderData> indexService,
            IOptions<ExchangeServiceSettings> settings,
            IEventService eventService,
            ISubscriptionService subscriptionService,
            IBalanceService balanceService,
            ITransactionService transactionService,
            IAssetService assetService,
            ILoggingService logger,
            IMemoryCache cache)
            : base(repository, cacheService, indexService, logger, eventService)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            // retry for exchange calls
            _retryPolicy = Policy
                .Handle<Exception>(ex => !(ex is ArgumentException || ex is ValidationException))
                .WaitAndRetryAsync(3, i => TimeSpan.FromSeconds(Math.Pow(2, i)),
                    (ex, ts, count, ctx) => Logger.LogWarning("Retry {Count} for operation {Op}", count, ctx?["Operation"]));

            // init exchanges
            if (_settings.Value.ExchangeSettings != null)
                InitExchanges(_settings.Value.ExchangeSettings);

            // parse fiat asset id
            if (Guid.TryParse(_settings.Value.PlatformFiatAssetId, out var fid))
                FiatAssetId = fid;
            else
                Logger.LogError("Invalid PlatformFiatAssetId");
            _assetService = assetService ?? throw new ArgumentNullException(nameof(_assetService));
        }

        private void InitExchanges(IDictionary<string, ExchangeSettings> cfg)
        {
            foreach (var kv in cfg)
            {
                try
                {
                    switch (kv.Key.ToLowerInvariant())
                    {
                        case "binance":
                            _exchanges.Add(kv.Key, new BinanceService(kv.Value, Logger));
                            Logger.LogInformation("Initialized Binance");
                            break;
                        default:
                            Logger.LogWarning("Skipping unsupported exchange {Name}", kv.Key);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError("Failed init {Name}", kv.Key);
                }
            }
        }

        public async Task<ResultWrapper<decimal>> GetCachedAssetPriceAsync(string ticker)
        {
            try
            {
                var assetResult = await _assetService.GetByTickerAsync(ticker);
                if (assetResult == null || !assetResult.IsSuccess)
                    throw new AssetFetchException();
                var asset = assetResult.Data;
                var exch = asset.Exchange;
                return await GetCachedAssetPriceAsync(exch, ticker);
            }
            catch (Exception ex)
            {
                return ResultWrapper<decimal>.FromException(ex);
            }
            
        }
        public Task<ResultWrapper<decimal>> GetCachedAssetPriceAsync(string exch, string ticker)
        {
            if (string.IsNullOrEmpty(exch) || string.IsNullOrEmpty(ticker))
                return Task.FromResult(ResultWrapper<decimal>.Failure(FailureReason.ValidationError, "Exchange and ticker required"));

            var key = $"price:{exch}:{ticker}";

            return FetchCached(
                key,
                async () =>
                {
                    if (!_exchanges.TryGetValue(exch, out var inst))
                        throw new ValidationException($"Unknown exchange {exch}", new() { ["exchange"] = [exch] });

                    var pr = await inst.GetAssetPrice(ticker);
                    if (!pr.IsSuccess) throw new AssetFetchException(pr.ErrorMessage);
                    return pr.Data;
                },
                PriceCacheDuration,
                () => new AssetFetchException($"Failed price {exch}:{ticker}"));
        }

        public Task<ResultWrapper<ExchangeBalance>> GetCachedExchangeBalanceAsync(string exch, string ticker)
        {
            if (string.IsNullOrEmpty(exch) || string.IsNullOrEmpty(ticker))
                throw new ValidationException("Exchange and ticker required", new()
                {
                    ["exchange"] = [exch],
                    ["ticker"] = [ticker],
                });

            var key = $"balance:{exch}:{ticker}";
            return FetchCached(
                key,
                async () =>
                {
                    if (!_exchanges.TryGetValue(exch, out var inst))
                        throw new ValidationException($"Unknown exchange {exch}", new() { ["exchange"] = new[] { exch } });

                    var br = await inst.GetBalanceAsync(ticker);
                    if (!br.IsSuccess) throw new BalanceFetchException(br.ErrorMessage);
                    return br.Data;
                },
                BalanceCacheDuration,
                () => new BalanceFetchException($"Failed balance {exch}:{ticker}"));
        }

        public async Task<ResultWrapper<IEnumerable<ExchangeOrderData>>> GetPendingOrdersAsync(CancellationToken ct = default)
        {
            try
            {
                var filter = Builders<ExchangeOrderData>.Filter.Eq(o => o.Status, OrderStatus.Pending);
                var wr = await GetManyAsync(filter, ct);
                return ResultWrapper<IEnumerable<ExchangeOrderData>>.Success(wr.Data.AsEnumerable());
            }
            catch (Exception ex)
            {
                Logger.LogError("GetPendingOrders failed");
                return ResultWrapper<IEnumerable<ExchangeOrderData>>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<ExchangeOrderData>> CreateOrderAsync(ExchangeOrderData order, CancellationToken ct = default)
        {
            try
            {
                if (order == null) throw new ArgumentNullException(nameof(order));
                order.Id = order.Id == Guid.Empty ? Guid.NewGuid() : order.Id;
                order.CreatedAt = order.CreatedAt == default ? DateTime.UtcNow : order.CreatedAt;

                var ins = await InsertAsync(order, ct);
                if (!ins.IsSuccess) throw new DatabaseException(ins.ErrorMessage);
                return ResultWrapper<ExchangeOrderData>.Success(order);
            }
            catch (Exception ex)
            {
                Logger.LogError("CreateOrderAsync failed");
                return ResultWrapper<ExchangeOrderData>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<bool>> UpdateOrderStatusAsync(Guid orderId, string status, CancellationToken ct = default)
        {
            try
            {
                if (orderId == Guid.Empty) throw new ArgumentException("Invalid orderId");
                if (string.IsNullOrEmpty(status)) throw new ArgumentException("Status required");

                var upd = await UpdateAsync(orderId, new { Status = status }, ct);
                if (!upd.IsSuccess) throw new DatabaseException(upd.ErrorMessage);
                return ResultWrapper<bool>.Success(true);
            }
            catch (Exception ex)
            {
                Logger.LogError("UpdateOrderStatusAsync failed");
                return ResultWrapper<bool>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<PaginatedResult<ExchangeOrderData>>> GetOrdersAsync(
            Guid? userId = null,
            Guid? subscriptionId = null,
            string status = null,
            Guid? assetId = null,
            int page = 1, int pageSize = 20,
            CancellationToken ct = default)
        {
            try
            {
                var filters = new List<FilterDefinition<ExchangeOrderData>>();
                var fb = Builders<ExchangeOrderData>.Filter;
                if (userId.HasValue) filters.Add(fb.Eq(o => o.UserId, userId.Value));
                if (subscriptionId.HasValue) filters.Add(fb.Eq(o => o.SubscriptionId, subscriptionId.Value));
                if (!string.IsNullOrEmpty(status)) filters.Add(fb.Eq(o => o.Status, status));
                if (assetId.HasValue) filters.Add(fb.Eq(o => o.AssetId, assetId.Value));

                var filter = filters.Any() ? fb.And(filters) : fb.Empty;
                var wr = await GetPaginatedAsync(filter, page, pageSize, nameof(ExchangeOrderData.CreatedAt), false, ct);
                return wr;
            }
            catch (Exception ex)
            {
                Logger.LogError("GetOrdersAsync failed");
                return ResultWrapper<PaginatedResult<ExchangeOrderData>>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<IEnumerable<ExchangeOrderData>>> GetOrdersByPaymentProviderIdAsync(string pid, CancellationToken ct = default)
        {
            try
            {
                if (string.IsNullOrEmpty(pid)) throw new ArgumentException("paymentProviderId required");
                var filter = Builders<ExchangeOrderData>.Filter.Eq(o => o.PaymentProviderId, pid);
                var wr = await GetManyAsync(filter, ct);
                return ResultWrapper<IEnumerable<ExchangeOrderData>>.Success(wr.Data.AsEnumerable());
            }
            catch (Exception ex)
            {
                Logger.LogError("GetOrdersByPaymentProviderIdAsync failed");
                return ResultWrapper<IEnumerable<ExchangeOrderData>>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<bool>> IsExchangeAvailableAsync(string exchangeName, CancellationToken cancellationToken = default)
        {
            try
            {
                if (string.IsNullOrEmpty(exchangeName) || !_exchanges.TryGetValue(exchangeName, out var exchange))
                    return ResultWrapper<bool>.Success(false, $"Exchange '{exchangeName}' is not configured");

                return await _retryPolicy.ExecuteAsync(async (ctx, ct) =>
                {
                    ctx["Operation"] = $"HealthCheck_{exchangeName}";
                    var balanceResult = await exchange.GetBalancesAsync();
                    return ResultWrapper<bool>.Success(balanceResult.IsSuccess);
                }, new Context(), cancellationToken);
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to check exchange availability");
                return ResultWrapper<bool>.FromException(ex);
            }
        }
    }
}
