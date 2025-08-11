using Application.Interfaces;
using Application.Interfaces.Asset;
using Application.Interfaces.Exchange;
using Application.Interfaces.Subscription;
using Binan‌​ceLibrary;
using Domain.Constants.Logging;
using Domain.DTOs;
using Domain.DTOs.Exchange;
using Domain.DTOs.Logging;
using Domain.Exceptions;
using Domain.Models.Exchange;
using Infrastructure.Services.Base;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Infrastructure.Services.Exchange
{
    public class ExchangeService : BaseService<ExchangeOrderData>, IExchangeService
    {
        private readonly Dictionary<string, IExchange> _exchanges = new(StringComparer.OrdinalIgnoreCase);
        private readonly IOptions<ExchangeServiceSettings> _settings;
        private readonly IAssetService _assetService;

        private const string ASSET_PRICE_CATCHE_FORMAT = "price:{0}:{1}";
        private static readonly TimeSpan ASSET_PRICE_CACHE_DURATION = TimeSpan.FromSeconds(30);


        public IReadOnlyDictionary<string, IExchange> Exchanges => _exchanges;
        public IExchange DefaultExchange => _exchanges.Values.FirstOrDefault()
            ?? throw new InvalidOperationException("No exchanges configured");
        public Guid FiatAssetId { get; }

        public ExchangeService(
            IOptions<ExchangeServiceSettings> settings,
            ISubscriptionService subscriptionService,
            IBalanceService balanceService,
            ITransactionService transactionService,
            IAssetService assetService,
            IServiceProvider serviceProvider)
            : base(serviceProvider)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            // init exchanges
            if (_settings.Value.ExchangeSettings != null)
                InitExchanges(_settings.Value.ExchangeSettings);

            // parse fiat asset id
            if (Guid.TryParse(_settings.Value.PlatformFiatAssetId, out var fid))
                FiatAssetId = fid;
            else
                _loggingService.LogError("Invalid PlatformFiatAssetId");
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
                            _exchanges.Add(kv.Key, new BinanceService(kv.Value, _loggingService));
                            _loggingService.LogInformation("Initialized Binance");
                            break;
                        default:
                            _loggingService.LogWarning("Skipping unsupported exchange {Name}", kv.Key);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    _loggingService.LogError("Failed init {Name}", kv.Key);
                }
            }
        }

        public async Task<ResultWrapper<decimal>> GetCachedAssetPriceAsync(string ticker)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Exchange",
                    FileName = "ExchangeService",
                    OperationName = "GetCachedAssetPriceAsync(string ticker)",
                    State = {
                        ["Ticker"] = ticker,
                    },
                    LogLevel = LogLevel.Critical
                },
                async () =>
                {
                    var assetResult = await _assetService.GetByTickerAsync(ticker);
                    if (assetResult == null || !assetResult.IsSuccess)
                        throw new AssetFetchException();
                    var asset = assetResult.Data;
                    var exchangeName = asset.Exchange;
                    var key = string.Format(ASSET_PRICE_CATCHE_FORMAT, exchangeName, ticker);

                    var cachedPrice = await _cacheService.GetAnyCachedAsync(
                        key,
                        async () =>
                        {
                            if (!_exchanges.TryGetValue(exchangeName, out var exchange))
                                throw new ValidationException($"Unknown exchange {exchangeName}", new() { ["exchange"] = [exchangeName] });

                            var pr = await exchange.GetAssetPrice(ticker);
                            if (!pr.IsSuccess) throw new AssetFetchException(pr.ErrorMessage);
                            return pr.Data;
                        },
                        ASSET_PRICE_CACHE_DURATION);

                    return cachedPrice;
                })
                .WithExchangeOperationResilience()
                .WithPerformanceMonitoring(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15))
                .WithPerformanceThreshold(TimeSpan.FromSeconds(3))
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<List<ExchangeOrderData>>> GetPendingOrdersAsync(CancellationToken ct = default)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Exchange",
                    FileName = "ExchangeService",
                    OperationName = "GetPendingOrdersAsync(CancellationToken ct = default)",
                    State = [],
                    LogLevel = LogLevel.Critical
                },
                async () =>
                {
                    var filter = Builders<ExchangeOrderData>.Filter.Eq(o => o.Status, Domain.Constants.OrderStatus.Pending);
                    var pendingOrdersResult = await GetManyAsync(filter, ct);
                    if( pendingOrdersResult == null || !pendingOrdersResult.IsSuccess)
                        throw new OrderFetchException("Failed to fetch pending orders");
                    return pendingOrdersResult.Data;
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<CrudResult<ExchangeOrderData>>> CreateOrderAsync(ExchangeOrderData order, CancellationToken ct = default)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Exchange",
                    FileName = "ExchangeService",
                    OperationName = "CreateOrderAsync(ExchangeOrderData order, CancellationToken ct = default)",
                    State = {
                        ["ExchangeOrder"] = order,
                    },
                    LogLevel = LogLevel.Critical
                },
                async () =>
                {
                    if (order == null) throw new ArgumentNullException(nameof(order));

                    order.Id = order.Id == Guid.Empty ? Guid.NewGuid() : order.Id;

                    var ins = await InsertAsync(order, ct);
                    if (ins == null || !ins.IsSuccess) throw new DatabaseException(ins?.ErrorMessage ?? "Insert result returned null");
                    return ins.Data;
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<CrudResult<ExchangeOrderData>>> UpdateOrderStatusAsync(Guid orderId, string status, CancellationToken ct = default)
        {
            return await _resilienceService.CreateBuilder(
                 new Scope
                 {
                     NameSpace = "Infrastructure.Services.Exchange",
                     FileName = "ExchangeService",
                     OperationName = "UpdateOrderStatusAsync(Guid orderId, string status, CancellationToken ct = default)",
                     State = {
                        ["OrderId"] = orderId,
                        ["Status"] = status,
                     },
                     LogLevel = LogLevel.Critical
                 },
                 async () =>
                 {
                     if (orderId == Guid.Empty) throw new ArgumentException("Invalid orderId");
                     if (string.IsNullOrEmpty(status)) throw new ArgumentException("Status required");

                     var updateResult = await UpdateAsync(orderId, new { Status = status }, ct);
                     if (updateResult == null || updateResult.IsSuccess) throw new DatabaseException(updateResult?.ErrorMessage ?? "Update result returned null");
                     return updateResult.Data;
                 })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<PaginatedResult<ExchangeOrderData>>> GetOrdersAsync(
            Guid? userId = null,
            Guid? subscriptionId = null,
            string? status = null,
            Guid? assetId = null,
            int page = 1, 
            int pageSize = 20,
            CancellationToken ct = default)
        {
            return await _resilienceService.CreateBuilder(
                 new Scope
                 {
                     NameSpace = "Infrastructure.Services.Exchange",
                     FileName = "ExchangeService",
                     OperationName = "GetOrdersAsync(Guid? userId = null, Guid? subscriptionId = null, string? status = null, Guid? assetId = null, int page = 1, int pageSize = 20, CancellationToken ct = default)",
                     State = {
                        ["UserId"] = userId,
                        ["SubscriptionId"] = subscriptionId,
                        ["Status"] = status,
                        ["AssetId"] = assetId,
                     },
                     LogLevel = LogLevel.Critical
                 },
                 async () =>
                 {
                     var filters = new List<FilterDefinition<ExchangeOrderData>>();
                     var fb = Builders<ExchangeOrderData>.Filter;
                     if (userId.HasValue) filters.Add(fb.Eq(o => o.UserId, userId.Value));
                     if (subscriptionId.HasValue) filters.Add(fb.Eq(o => o.SubscriptionId, subscriptionId.Value));
                     if (!string.IsNullOrEmpty(status)) filters.Add(fb.Eq(o => o.Status, status));
                     if (assetId.HasValue) filters.Add(fb.Eq(o => o.AssetId, assetId.Value));

                     var filter = filters.Any() ? fb.And(filters) : fb.Empty;
                     var ordersResult = await GetPaginatedAsync(filter, page, pageSize, nameof(ExchangeOrderData.CreatedAt), false, ct);
                     if (ordersResult == null || !ordersResult.IsSuccess)
                         throw new OrderFetchException(ordersResult?.ErrorMessage ?? "Paginated order fetch result returned null");
                     return ordersResult.Data;
                 })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<List<ExchangeOrderData>>> GetOrdersByPaymentProviderIdAsync(string pid, CancellationToken ct = default)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Exchange",
                    FileName = "ExchangeService",
                    OperationName = "GetOrdersByPaymentProviderIdAsync(string pid, CancellationToken ct = default)",
                    State = {
                    ["PaymentProviderId"] = pid,
                    },
                    LogLevel = LogLevel.Critical
                },
                async () =>
                {
                    if (string.IsNullOrEmpty(pid)) throw new ArgumentException("paymentProviderId required");
                    var filter = Builders<ExchangeOrderData>.Filter.Eq(o => o.PaymentProviderId, pid);
                    var wr = await GetManyAsync(filter, ct);
                    if (wr == null || !wr.IsSuccess)
                        throw new OrderFetchException(wr?.ErrorMessage ?? "Order fetch result returned null");
                    return wr.Data;
                }).ExecuteAsync();
        }

        public async Task<ResultWrapper<decimal>> GetMinNotionalAsync(string ticker, string? exchange = null, CancellationToken ct = default)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Exchange",
                    FileName = "ExchangeService",
                    OperationName = "GetMinNotionalAsync(string ticker, string? exchange = null, CancellationToken ct = default)",
                    State = {
                        ["Ticker"] = ticker,
                        ["Exchange"] = exchange ?? "auto-detect",
                    },
                    LogLevel = LogLevel.Critical
                },
                async () =>
                {
                    if (string.IsNullOrEmpty(ticker))
                        throw new ArgumentException("Asset ticker is required", nameof(ticker));

                    // Get asset from database to determine exchange
                    var assetResult = await _assetService.GetByTickerAsync(ticker);
                    if (assetResult == null || !assetResult.IsSuccess || assetResult.Data == null)
                        throw new AssetFetchException($"Asset with ticker '{ticker}' not found");

                    var asset = assetResult.Data;

                    // Use provided exchange or default to asset's exchange
                    var exchangeName = !string.IsNullOrEmpty(exchange) ? exchange : asset.Exchange;

                    // Verify exchange exists
                    if (!_exchanges.TryGetValue(exchangeName, out var exchangeInstance))
                        throw new ExchangeApiException($"Exchange '{exchangeName}' not supported");

                    // Create cache key
                    var cacheKey = $"min_notional:{exchangeName}:{ticker}";
                    var cacheDuration = TimeSpan.FromHours(1);

                    // Get cached or fetch from exchange
                    var minNotional = await _cacheService.GetAnyCachedAsync(
                        cacheKey,
                        async () =>
                        {
                            var result = await exchangeInstance.GetMinNotional(ticker);
                            if (!result.IsSuccess)
                                throw new ExchangeApiException($"Failed to get minimum notional for {ticker}: {result.ErrorMessage}", exchangeName);

                            return result.Data;
                        },
                        cacheDuration);

                    return minNotional;
                })
                .WithExchangeOperationResilience()
                .WithPerformanceMonitoring(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15))
                .WithPerformanceThreshold(TimeSpan.FromSeconds(3))
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<Dictionary<string, decimal>>> GetMinNotionalsAsync(string[] tickers, CancellationToken ct = default)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Exchange",
                    FileName = "ExchangeService",
                    OperationName = "GetMinNotionalsAsync(string[] tickers, CancellationToken ct = default)",
                    State = {
                ["TickerCount"] = tickers.Length,
                ["Tickers"] = string.Join(",", tickers),
                    },
                    LogLevel = LogLevel.Critical
                },
                async () =>
                {
                    if (tickers == null || tickers.Length == 0)
                        throw new ArgumentException("At least one ticker is required", nameof(tickers));

                    // Get assets from database to determine exchanges
                    var assetsResult = await _assetService.GetManyByTickersAsync(tickers);
                    if (assetsResult == null || !assetsResult.IsSuccess || assetsResult.Data == null || !assetsResult.Data.Any())
                        throw new AssetFetchException("None of the requested tickers were found");

                    // Group assets by exchange
                    var assetsByExchange = assetsResult.Data
                        .GroupBy(a => a.Exchange)
                        .ToDictionary(g => g.Key, g => g.ToList());

                    var result = new Dictionary<string, decimal>();
                    var cacheDuration = TimeSpan.FromHours(1);

                    // Process each exchange group in parallel
                    var tasks = assetsByExchange.Select(async exchangeGroup =>
                    {
                        var exchangeName = exchangeGroup.Key;
                        var exchangeAssets = exchangeGroup.Value;

                        // Verify exchange exists
                        if (!_exchanges.TryGetValue(exchangeName, out var exchange))
                        {
                            _loggingService.LogWarning("Exchange {Exchange} not supported, skipping tickers", exchangeName);
                            return;
                        }

                        var exchangeTickers = exchangeAssets.Select(a => a.Ticker).ToArray();

                        try
                        {
                            // Step 1: Check individual cache entries first
                            var cachedResults = new Dictionary<string, decimal>();
                            var uncachedTickers = new List<string>();

                            foreach (var ticker in exchangeTickers)
                            {
                                var individualCacheKey = $"min_notional:{exchangeName}:{ticker}";
                                if (_cacheService.TryGetValue(individualCacheKey, out decimal cachedValue))
                                {
                                    cachedResults[ticker] = cachedValue;
                                    _loggingService.LogInformation("Cache hit for min notional: {Exchange}:{Ticker}", exchangeName, ticker);
                                }
                                else
                                {
                                    uncachedTickers.Add(ticker);
                                }
                            }

                            // Step 2: Fetch remaining tickers from exchange if any
                            Dictionary<string, decimal>? fetchedResults = null;
                            if (uncachedTickers.Any())
                            {
                                _loggingService.LogInformation("Fetching {Count} uncached min notionals from {Exchange}",
                                    uncachedTickers.Count, exchangeName);

                                var minNotionalsResult = await exchange.GetMinNotionals(uncachedTickers.ToArray());
                                if (!minNotionalsResult.IsSuccess || minNotionalsResult.Data == null)
                                    throw new ExchangeApiException($"Failed to get minimum notionals from {exchangeName}: {minNotionalsResult?.ErrorMessage ?? "Unknown error"}", exchangeName);

                                fetchedResults = minNotionalsResult.Data;

                                // Step 3: Cache individual results for future use
                                foreach (var ticker in uncachedTickers)
                                {
                                    var symbol = $"{ticker}{exchange.QuoteAssetTicker}";
                                    if (fetchedResults.TryGetValue(symbol, out decimal minNotional))
                                    {
                                        var individualCacheKey = $"min_notional:{exchangeName}:{ticker}";
                                        _cacheService.Set(individualCacheKey, minNotional, cacheDuration);
                                        cachedResults[ticker] = minNotional;
                                    }
                                }
                            }

                            // Step 4: Combine cached and fetched results
                            foreach (var asset in exchangeAssets)
                            {
                                if (cachedResults.TryGetValue(asset.Ticker, out decimal minNotional))
                                {
                                    lock (result)
                                    {
                                        result[asset.Ticker] = minNotional;
                                    }
                                }
                            }

                            _loggingService.LogInformation("Min notionals for {Exchange}: {CachedCount} cached, {FetchedCount} fetched",
                                exchangeName, cachedResults.Count - uncachedTickers.Count, uncachedTickers.Count);
                        }
                        catch (Exception ex)
                        {
                            _loggingService.LogError("Failed to get min notionals from {Exchange}: {Error}",
                                exchangeName, ex.Message);
                        }
                    });

                    // Wait for all exchange calls to complete
                    await Task.WhenAll(tasks);

                    return result;
                })
                .WithExchangeOperationResilience()
                .WithPerformanceMonitoring(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30))
                .WithPerformanceThreshold(TimeSpan.FromSeconds(5))
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<bool>> IsExchangeAvailableAsync(string exchangeName, CancellationToken cancellationToken = default)
        {
            return await _resilienceService.CreateBuilder(
                 new Scope
                 {
                     NameSpace = "Infrastructure.Services.Exchange",
                     FileName = "ExchangeService",
                     OperationName = "IsExchangeAvailableAsync(string exchangeName, CancellationToken cancellationToken = default)",
                     State = {
                        ["ExchangeName"] = exchangeName,
                     },
                     LogLevel = LogLevel.Critical
                 },
                 async () =>
                 {
                     if (string.IsNullOrEmpty(exchangeName) || !_exchanges.TryGetValue(exchangeName, out var exchange))
                         throw new ExchangeApiException($"Exchange '{exchangeName}' is not configured", exchangeName);

                     var balanceResult = await exchange.GetBalancesAsync();
                     if (balanceResult == null) throw new ExchangeApiException("Balance fetch result returned null", exchangeName);
                     return balanceResult.IsSuccess;
                 })
                .WithExchangeOperationResilience()
                .WithPerformanceMonitoring(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30))
                .WithPerformanceThreshold(TimeSpan.FromSeconds(5))
                .ExecuteAsync();
        }
    }
}
