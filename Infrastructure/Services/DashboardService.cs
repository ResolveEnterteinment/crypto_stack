using Application.Interfaces;
using Application.Interfaces.Asset;
using Application.Interfaces.Exchange;
using Application.Interfaces.Payment;
using Application.Interfaces.Subscription;
using Domain.DTOs;
using Domain.DTOs.Dashboard;
using Domain.DTOs.Logging;
using Domain.Events;
using Domain.Events.Entity;
using Domain.Exceptions;
using Domain.Models.Balance;
using Domain.Models.Dashboard;
using Infrastructure.Hubs;
using Infrastructure.Services.Base;
using Microsoft.AspNetCore.SignalR;
using MongoDB.Driver;
using MongoDB.Driver.Linq;

namespace Infrastructure.Services
{
    public class DashboardService :
        BaseService<DashboardData>,
        IDashboardService
    {
        private readonly IExchangeService _exchangeService;
        private readonly ISubscriptionService _subscriptionService;
        private readonly IBalanceService _balanceService;
        private readonly IHubContext<DashboardHub> _hubContext;

        // Cache keys and durations
        private const string DASHBOARD_DTO_CACHE_KEY = "dashboard_dto:{0}";
        private const string TOTAL_INVESTMENTS_CACHE_KEY = "total_investments:{0}";
        private const string ASSET_HOLDINGS_CACHE_KEY = "asset_holdings:{0}";
        private const string ASSET_HOLDINGS_TYPED_CACHE_KEY = "asset_holdings:{0}:{1}";

        private static readonly TimeSpan DASHBOARD_CACHE_DURATION = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan INVESTMENTS_CACHE_DURATION = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan HOLDINGS_CACHE_DURATION = TimeSpan.FromMinutes(5);

        public DashboardService(
            IServiceProvider serviceProvider,
            IExchangeService exchangeService,
            ISubscriptionService subscriptionService,
            IPaymentService paymentService,
            IAssetService assetService,
            IBalanceService balanceService,
            IHubContext<DashboardHub> hubContext
        ) : base(
            serviceProvider,
            new()
            {
                IndexModels = [
                new CreateIndexModel<DashboardData>(Builders<DashboardData>.IndexKeys.Ascending(d => d.UserId), new CreateIndexOptions { Name = "UserId_1", Unique = true })
                    ]
            }
        )
        {
            _exchangeService = exchangeService ?? throw new ArgumentNullException(nameof(exchangeService));
            _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
            _balanceService = balanceService ?? throw new ArgumentNullException(nameof(balanceService));
            _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
        }

        public async Task<ResultWrapper<DashboardDto>> GetDashboardDataAsync(Guid userId)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services",
                    FileName = "DashboardService",
                    OperationName = "GetDashboardDataAsync(Guid userId)",
                    State = { ["UserId"] = userId },
                    LogLevel = Domain.Constants.Logging.LogLevel.Error
                },
                async () =>
                {
                    if (userId == Guid.Empty)
                    {
                        throw new ArgumentException("Invalid userId");
                    }

                    // Try to get cached dashboard DTO first
                    string cacheKey = string.Format(DASHBOARD_DTO_CACHE_KEY, userId);

                    var cachedDashboard = await _cacheService.GetAnyCachedAsync<DashboardDto>(
                        cacheKey,
                        async () =>
                        {
                            // Fetch data concurrently
                            var inv = FetchTotalInvestmentsCachedAsync(userId);
                            var holdings = FetchAssetHoldingsCachedAsync(userId);
                            await Task.WhenAll(inv, holdings);

                            var invRes = await inv;
                            var holdRes = await holdings;

                            if (!invRes.IsSuccess)
                            {
                                throw new Exception(invRes.ErrorMessage);
                            }

                            if (!holdRes.IsSuccess)
                            {
                                throw new Exception(holdRes.ErrorMessage);
                            }

                            decimal total = invRes.Data;
                            var assets = holdRes.Data;
                            decimal portfolio = assets.Sum(a => a.Value);

                            // Persist update (fire and forget)
                            _ = Task.Run(() => UpdateDashboardData(userId, total, assets, portfolio));

                            return new DashboardDto
                            {
                                TotalInvestments = total,
                                AssetHoldings = assets,
                                PortfolioValue = portfolio
                            };
                        },
                        DASHBOARD_CACHE_DURATION
                    );

                    if (cachedDashboard == null)
                    {
                        throw new DashboardException("Failed to retrieve dashboard data");
                    }

                    return cachedDashboard;
                })
                .WithMongoDbReadResilience()
                .WithPerformanceMonitoring(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5))
                .OnError(async ex =>
                {
                    await _loggingService.LogTraceAsync(
                        $"Failed to fetch dashboard data for user {userId}: {ex.Message}",
                        "GetDashboardDataAsync",
                        Domain.Constants.Logging.LogLevel.Error,
                        requiresResolution: true);
                })
                .ExecuteAsync();
        }


        public async Task<ResultWrapper> UpdateDashboardData(Guid userId, decimal total, IEnumerable<AssetHoldingDto> assets, decimal portfolio)
        {
            return await _resilienceService.CreateBuilder(
                new Domain.DTOs.Logging.Scope
                {
                    NameSpace = "Infrastructure.Services",
                    FileName = "DashboardService",
                    OperationName = "UpdateDashboardData(Guid userId, decimal total, IEnumerable<AssetHoldingDto> assets, decimal portfolio)",
                    State = {
                        ["UserId"] = userId,
                        ["TotalInvestments"] = total,
                        ["PortfolioValue"] = portfolio,
                        ["AssetCount"] = assets.Count()
                    },
                    LogLevel = Domain.Constants.Logging.LogLevel.Error
                },
                async () =>
                {
                    // Upsert by userId
                    var filter = Builders<DashboardData>.Filter.Eq(d => d.UserId, userId);
                    var existingWr = await GetOneAsync(filter);
                    var fields = new { TotalInvestments = total, AssetHoldings = assets, PortfolioValue = portfolio };

                    DashboardData updatedData;
                    if (existingWr.Data != null)
                    {
                        var updateResult = await UpdateAsync(existingWr.Data.Id, fields);

                        if (updateResult == null || !updateResult.IsSuccess || !updateResult.Data.IsSuccess)
                            throw new DatabaseException($"Failed to update dashboard: {updateResult?.ErrorMessage ?? "Update result returned null"}");

                        // Create updated data object for caching
                        updatedData = existingWr.Data;
                        updatedData.TotalInvestments = total;
                        updatedData.AssetHoldings = assets;
                        updatedData.PortfolioValue = portfolio;
                        updatedData.UpdatedAt = DateTime.UtcNow;
                    }
                    else
                    {
                        var newData = new DashboardData
                        {
                            UserId = userId,
                            TotalInvestments = total,
                            AssetHoldings = assets,
                            PortfolioValue = portfolio
                        };
                        var insertResult = await InsertAsync(newData);

                        if (insertResult == null || !insertResult.IsSuccess || !insertResult.Data.IsSuccess)
                            throw new DatabaseException($"Failed to insert dashboard: {insertResult?.ErrorMessage ?? "Insert result returned null"}");

                        updatedData = newData;
                    }

                    // Update all related caches
                    await UpdateAllRelatedCachesAsync(userId, total, assets, portfolio, updatedData);
                })
                .WithMongoDbWriteResilience()
                .WithPerformanceMonitoring(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4))
                .OnError(async ex =>
                {
                    await _loggingService.LogTraceAsync(
                        $"Failed to update dashboard data for user {userId}: {ex.Message}",
                        "UpdateDashboardData",
                        Domain.Constants.Logging.LogLevel.Error,
                        requiresResolution: true);
                })
                .ExecuteAsync();
        }

        private async Task UpdateAllRelatedCachesAsync(Guid userId, decimal total, IEnumerable<AssetHoldingDto> assets, decimal portfolio, DashboardData data)
        {
            try
            {
                // Update entity cache
                string entityCacheKey = _cacheService.GetCacheKey(data.Id);
                _cacheService.Set(entityCacheKey, data, DASHBOARD_CACHE_DURATION);

                // Update user-specific caches
                string userEntityCacheKey = userId.ToString();
                _cacheService.Set(userEntityCacheKey, data, DASHBOARD_CACHE_DURATION);

                // Update dashboard DTO cache
                string dashboardDtoCacheKey = string.Format(DASHBOARD_DTO_CACHE_KEY, userId);
                var dashboardDto = new DashboardDto
                {
                    TotalInvestments = total,
                    AssetHoldings = assets,
                    PortfolioValue = portfolio
                };
                _cacheService.Set(dashboardDtoCacheKey, dashboardDto, DASHBOARD_CACHE_DURATION);

                // Update component caches
                string totalInvestmentsCacheKey = string.Format(TOTAL_INVESTMENTS_CACHE_KEY, userId);
                _cacheService.Set(totalInvestmentsCacheKey, total, INVESTMENTS_CACHE_DURATION);

                string assetHoldingsCacheKey = string.Format(ASSET_HOLDINGS_CACHE_KEY, userId);
                _cacheService.Set(assetHoldingsCacheKey, assets, HOLDINGS_CACHE_DURATION);

                _loggingService.LogInformation("Updated all dashboard caches for user {UserId}", userId);
            }
            catch (Exception ex)
            {
                // Log cache update errors but don't fail the operation
                _loggingService.LogError("Failed to update dashboard caches for user {UserId}: {Error}", userId, ex.Message);
            }
        }

        public void InvalidateDashboardCacheAsync(Guid userId)
        {
            try
            {
                // Invalidate all related cache keys
                var cacheKeys = new[]
                {
                    string.Format(DASHBOARD_DTO_CACHE_KEY, userId),
                    string.Format(TOTAL_INVESTMENTS_CACHE_KEY, userId),
                    string.Format(ASSET_HOLDINGS_CACHE_KEY, userId),
                    userId.ToString() // User-specific entity cache
                };

                foreach (var key in cacheKeys)
                {
                    _cacheService.Invalidate(key);
                }

                // Also invalidate typed asset holdings caches (common asset types)
                var commonAssetTypes = new[] { "Exchange", "Staking", "DeFi" };
                foreach (var assetType in commonAssetTypes)
                {
                    string typedKey = string.Format(ASSET_HOLDINGS_TYPED_CACHE_KEY, userId, assetType);
                    _cacheService.Invalidate(typedKey);
                }

                _loggingService.LogInformation("Invalidated all dashboard caches for user {UserId}", userId);
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Failed to invalidate dashboard caches for user {UserId}: {Error}", userId, ex.Message);
            }
        }

        /// <summary>
        /// Invalidates dashboard cache for multiple users efficiently
        /// </summary>
        public void InvalidateDashboardCacheForUsers(IEnumerable<Guid> userIds)
        {
            try
            {
                foreach (var userId in userIds)
                {
                    InvalidateDashboardCacheAsync(userId);
                }

                _loggingService.LogInformation("Invalidated dashboard caches for {UserCount} users", userIds.Count());
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Failed to invalidate dashboard caches for multiple users: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Gets cached dashboard data entity by user ID
        /// </summary>
        public async Task<ResultWrapper<DashboardData?>> GetCachedDashboardEntityAsync(Guid userId)
        {
            return await FetchCached(
                userId.ToString(),
                async () =>
                {
                    var filter = Builders<DashboardData>.Filter.Eq(d => d.UserId, userId);
                    var result = await GetOneAsync(filter);
                    return result.IsSuccess ? result.Data : null;
                },
                DASHBOARD_CACHE_DURATION,
                () => new KeyNotFoundException($"Dashboard data not found for user {userId}")
            );
        }

        /// <summary>
        /// Warms up the cache for a specific user
        /// </summary>
        public async Task<ResultWrapper> WarmupUserCacheAsync(Guid userId)
        {
            try
            {
                _loggingService.LogInformation("Warming up dashboard cache for user {UserId}", userId);

                // Pre-load dashboard data
                var dashboardResult = await GetDashboardDataAsync(userId);

                if (dashboardResult.IsSuccess)
                {
                    _loggingService.LogInformation("Successfully warmed up dashboard cache for user {UserId}", userId);
                    return ResultWrapper.Success("Cache warmed up successfully");
                }
                else
                {
                    _loggingService.LogWarning("Failed to warm up dashboard cache for user {UserId}: {Error}",
                        userId, dashboardResult.ErrorMessage);
                    return ResultWrapper.Failure(
                        Domain.Constants.FailureReason.CacheOperationFailed,
                        $"Failed to warm up cache: {dashboardResult.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Error warming up dashboard cache for user {UserId}: {Error}", userId, ex.Message);
                return ResultWrapper.FromException(ex);
            }
        }

        /// <summary>
        /// Gets cache statistics for monitoring
        /// </summary>
        public async Task<ResultWrapper<DashboardCacheStats>> GetCacheStatsAsync(Guid userId)
        {
            try
            {
                var stats = new DashboardCacheStats
                {
                    UserId = userId,
                    DashboardDtoExists = _cacheService.TryGetValue<DashboardDto>(string.Format(DASHBOARD_DTO_CACHE_KEY, userId), out _),
                    TotalInvestmentsExists = _cacheService.TryGetValue<decimal>(string.Format(TOTAL_INVESTMENTS_CACHE_KEY, userId), out _),
                    AssetHoldingsExists = _cacheService.TryGetValue<AssetHoldingDto>(string.Format(ASSET_HOLDINGS_CACHE_KEY, userId), out _),
                    EntityExists = _cacheService.TryGetValue<DashboardData>(userId.ToString(), out _),
                    Timestamp = DateTime.UtcNow
                };

                return ResultWrapper<DashboardCacheStats>.Success(stats);
            }
            catch (Exception ex)
            {
                return ResultWrapper<DashboardCacheStats>.FromException(ex);
            }
        }

        public Task Handle(EntityUpdatedEvent<BalanceData> notification, CancellationToken ct)
        {
            InvalidateDashboardCacheAsync(notification.Entity.UserId);
            return InvalidateCacheAndPush(notification.Entity.UserId);
        }

        public Task Handle(EntityCreatedEvent<BalanceData> notification, CancellationToken ct)
        {
            InvalidateDashboardCacheAsync(notification.Entity.UserId);
            return InvalidateCacheAndPush(notification.Entity.UserId);
        }

        #region Private Methods
        private async Task InvalidateCacheAndPush(Guid userId)
        {
            try
            {
                // Invalidate cache first
                InvalidateDashboardCacheAsync(userId);

                // Fetch fresh data and push to SignalR clients
                var dashWr = await GetDashboardDataAsync(userId);
                if (dashWr.IsSuccess)
                {
                    await _hubContext.Clients.Group(userId.ToString()).SendAsync("DashboardUpdate", dashWr.Data);
                    _loggingService.LogInformation("Pushed fresh dashboard data to SignalR clients for user {UserId}", userId);
                }
                else
                {
                    _loggingService.LogWarning("Failed to fetch fresh dashboard data after cache invalidation for user {UserId}: {Error}",
                        userId, dashWr.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Error in InvalidateCacheAndPush for user {UserId}: {Error}", userId, ex.Message);
            }
        }

        private async Task<ResultWrapper<IEnumerable<AssetHoldingDto>>> FetchAssetHoldingsCachedAsync(Guid userId, string? assetType = null)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services",
                    FileName = "DashboardService",
                    OperationName = "FetchAssetHoldingsCachedAsync(Guid userId, string? assetType)",
                    State = { ["UserId"] = userId, ["AssetType"] = assetType ?? "All" },
                    LogLevel = Domain.Constants.Logging.LogLevel.Error
                },
                async () =>
                {
                    if (userId == Guid.Empty)
                    {
                        throw new ArgumentException("Invalid userId");
                    }

                    // Create cache key based on asset type
                    string cacheKey = string.IsNullOrEmpty(assetType)
                        ? string.Format(ASSET_HOLDINGS_CACHE_KEY, userId)
                        : string.Format(ASSET_HOLDINGS_TYPED_CACHE_KEY, userId, assetType);

                    var cachedHoldings = await _cacheService.GetAnyCachedAsync<IEnumerable<AssetHoldingDto>>(
                        cacheKey,
                        async () => await FetchAssetHoldingsFromSourceAsync(userId, assetType),
                        HOLDINGS_CACHE_DURATION
                    );

                    return cachedHoldings ?? Enumerable.Empty<AssetHoldingDto>();
                })
                .WithMongoDbReadResilience()
                .WithPerformanceMonitoring(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3))
                .ExecuteAsync();
        }

        private async Task<IEnumerable<AssetHoldingDto>> FetchAssetHoldingsFromSourceAsync(Guid userId, string? assetType = null)
        {
            var balancesResult = await _balanceService.FetchBalancesWithAssetsAsync(userId, assetType);
            if (balancesResult == null || !balancesResult.IsSuccess)
            {
                throw new BalanceFetchException($"Failed to fetch user {userId} balances.");
            }

            var balances = balancesResult.Data;
            var list = new List<AssetHoldingDto>();

            var exchangeGroups = balances.GroupBy(b => b.Asset!.Exchange);

            foreach (var group in exchangeGroups)
            {
                var groupBalances = group.ToList();

                var exchangeName = groupBalances.First().Asset!.Exchange;
                if (!_exchangeService.Exchanges.TryGetValue(exchangeName, out var exchange))
                    continue;

                var ratesResult = await exchange.GetAssetPrices(groupBalances.Select(balance => balance.Ticker)!);
                var rates = ratesResult.Data;

                foreach (var balance in group)
                {
                    var asset = balance.Asset!;
                    decimal val = 0;

                    if (rates?.TryGetValue(asset.Ticker, out var rate) ?? false)
                    {
                        val = balance.Total * rate;
                    }
                    else
                    {
                        val = balance.Total;
                    }

                    list.Add(new AssetHoldingDto
                    {
                        Id = asset.Id.ToString(),
                        Name = asset.Name,
                        Symbol = asset.Symbol,
                        Ticker = asset.Ticker,
                        Total = balance.Total,
                        Value = val
                    });
                }
            }

            return list;
        }

        private async Task<ResultWrapper<decimal>> FetchTotalInvestmentsCachedAsync(Guid userId)
        {
            return await _resilienceService.CreateBuilder(
                new Domain.DTOs.Logging.Scope
                {
                    NameSpace = "Infrastructure.Services",
                    FileName = "DashboardService",
                    OperationName = "FetchTotalInvestmentsCachedAsync(Guid userId)",
                    State = { ["UserId"] = userId },
                    LogLevel = Domain.Constants.Logging.LogLevel.Error
                },
                async () =>
                {
                    if (userId == Guid.Empty)
                    {
                        throw new ArgumentException("Invalid userId");
                    }

                    string cacheKey = string.Format(TOTAL_INVESTMENTS_CACHE_KEY, userId);

                    var cachedTotal = await _cacheService.GetAnyCachedAsync<decimal>(
                        cacheKey,
                        async () => await FetchTotalInvestmentsFromSourceAsync(userId),
                        INVESTMENTS_CACHE_DURATION
                    );

                    return cachedTotal;
                })
                .WithMongoDbReadResilience()
                .WithPerformanceMonitoring(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2))
                .ExecuteAsync();
        }

        private async Task<decimal> FetchTotalInvestmentsFromSourceAsync(Guid userId)
        {
            var subs = await _subscriptionService.GetAllByUserIdAsync(userId);
            if (!subs.IsSuccess)
            {
                throw new SubscriptionFetchException(subs.ErrorMessage);
            }

            return subs.Data.Sum(s => s.TotalInvestments);
        }
        #endregion
    }
}