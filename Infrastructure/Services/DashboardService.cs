using Application.Interfaces;
using Application.Interfaces.Asset;
using Application.Interfaces.Exchange;
using Application.Interfaces.Payment;
using Application.Interfaces.Withdrawal;
using Domain.Constants;
using Domain.DTOs;
using Domain.DTOs.Dashboard;
using Domain.DTOs.Logging;
using Domain.Events;
using Domain.Events.Entity;
using Domain.Exceptions;
using Domain.Models.Balance;
using Domain.Models.Dashboard;
using Domain.Models.Exchange;
using Domain.Models.Withdrawal;
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
        private readonly IBalanceService _balanceService;
        private readonly ITransactionService _transactionService;
        private readonly IWithdrawalService _withdrawalService;
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
            IPaymentService paymentService,
            IAssetService assetService,
            IBalanceService balanceService,
            ITransactionService transactionService,
            IWithdrawalService withdrawalService,
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
            _balanceService = balanceService ?? throw new ArgumentNullException(nameof(balanceService));
            _transactionService = transactionService ?? throw new ArgumentNullException(nameof(transactionService));
            _withdrawalService = withdrawalService ?? throw new ArgumentNullException(nameof(withdrawalService));
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

        public Task Handle(EntityCreatedEvent<ExchangeOrderData> notification, CancellationToken ct)
        {
            InvalidateDashboardCacheAsync(notification.Entity.UserId);
            return InvalidateCacheAndPush(notification.Entity.UserId);
        }

        public Task Handle(EntityCreatedEvent<WithdrawalData> notification, CancellationToken ct)
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

                var exchangeName = group.Key;
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
                new Scope
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
            var totalInvestments = 0m;

            // Calculate total investments for each EXCHANGE asset in user balances
            var balancesResult = await _balanceService.FetchBalancesWithAssetsAsync(userId, "Exchange");

            if (balancesResult == null || !balancesResult.IsSuccess)
            {
                throw new BalanceFetchException($"Failed to fetch user {userId} balances.");
            }

            var balances = balancesResult.Data;

            if (balances == null || balances.Count == 0)
            {
                return 0;
            }

            // Process each balance to calculate its investment value
            foreach (var balance in balances)
            {
                try
                {
                    // Fetch all exchange orders for this balance
                    var ordersResult = await _exchangeService.GetOrdersAsync(
                        userId: userId,
                        statuses: [OrderStatus.Filled, OrderStatus.PartiallyFilled],
                        assetId: balance.AssetId);

                    if (ordersResult == null || !ordersResult.IsSuccess)
                    {
                        _loggingService.LogError("Failed to fetch exchange orders for user {UserId} and asset {AssetId}: {Error}",
                            userId, balance.AssetId, ordersResult?.ErrorMessage ?? "Unknown error");
                        continue;
                    }

                    var orders = ordersResult.Data.Items;

                    if (orders == null || !orders.Any())
                    {
                        continue;
                    }

                    var buyOrders = orders
                        .Where(o => o.Side.Equals(OrderSide.Buy, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    var sellOrders = orders
                        .Where(o => o.Side.Equals(OrderSide.Sell, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    // Fetch withdrawal history for this specific asset
                    var withdrawalsResult = await _withdrawalService.GetUserWithdrawalHistoryAsync(userId, balance.Asset?.Ticker ?? balance.Ticker);

                    List<WithdrawalData> withdrawals = new();
                    if (withdrawalsResult != null && withdrawalsResult.IsSuccess && withdrawalsResult.Data != null)
                    {
                        // Only include completed withdrawals in FIFO calculation
                        withdrawals = withdrawalsResult.Data
                            .Where(w => w.Status.Equals("COMPLETED", StringComparison.OrdinalIgnoreCase))
                            .ToList();
                    }

                    if (buyOrders.Count == 0)
                    {
                        continue;
                    }

                    // Calculate total invested amount and total quantity purchased from BUYs
                    var totalBuyInvestment = buyOrders.Sum(t => t.QuoteQuantity);
                    var totalBuyQuantity = buyOrders.Sum(t => t.Quantity);

                    if (totalBuyQuantity <= 0)
                    {
                        continue;
                    }

                    // Calculate net quantity after sales and withdrawals
                    var totalSellQuantity = sellOrders.Sum(t => t.Quantity);
                    var totalWithdrawalQuantity = withdrawals.Sum(w => w.Amount);
                    var totalOutflowQuantity = totalSellQuantity + totalWithdrawalQuantity;

                    var netQuantity = totalBuyQuantity - totalOutflowQuantity;

                    // If net quantity is zero or negative, no investment remains
                    if (netQuantity <= 0)
                    {
                        _loggingService.LogInformation(
                            "Net quantity for asset {AssetTicker} is {NetQuantity} after withdrawals, skipping investment calculation",
                            balance.Asset?.Ticker ?? balance.Ticker,
                            netQuantity);
                        continue;
                    }

                    // Calculate weighted average rate using enhanced FIFO that includes withdrawals
                    var remainingCostBasis = CalculateRemainingCostBasisWithWithdrawals(
                        buyOrders, sellOrders, withdrawals);
                    var weightedAverageRate = remainingCostBasis / netQuantity;

                    // Calculate asset value based on current balance and weighted average investment rate
                    var assetValue = balance.Total * weightedAverageRate;
                    totalInvestments += assetValue ?? 0;

                    _loggingService.LogInformation(
                        "Calculated investment for asset {AssetTicker}: Balance={Balance}, WeightedAvgRate={WeightedAvgRate}, " +
                        "Value={Value}, TotalBuy={TotalBuy}, TotalSell={TotalSell}, TotalWithdrawals={TotalWithdrawals}, NetQty={NetQty}",
                        balance.Asset?.Ticker ?? balance.Ticker,
                        balance.Total,
                        weightedAverageRate,
                        assetValue,
                        totalBuyQuantity,
                        totalSellQuantity,
                        totalWithdrawalQuantity,
                        netQuantity);
                }
                catch (Exception ex)
                {
                    _loggingService.LogError("Error calculating investment for asset {AssetId}: {Error}",
                        balance.AssetId, ex.Message);
                    // Continue with other assets rather than failing the entire calculation
                    continue;
                }
            }

            return totalInvestments;
        }

        /// <summary>
        /// Calculates the remaining cost basis using FIFO accounting method including withdrawals
        /// </summary>
        /// <param name="buyOrders">List of buy transactions ordered by date</param>
        /// <param name="sellOrders">List of sell transactions ordered by date</param>
        /// <param name="withdrawals">List of completed withdrawals ordered by date</param>
        /// <returns>The cost basis of the remaining holdings</returns>
        private decimal CalculateRemainingCostBasisWithWithdrawals(
            List<ExchangeOrderData> buyOrders,
            List<ExchangeOrderData> sellOrders,
            List<WithdrawalData> withdrawals)
        {
            // Sort all transactions by date for FIFO calculation
            var sortedBuys = buyOrders.OrderBy(t => t.CreatedAt).ToList();
            var sortedSells = sellOrders.OrderBy(t => t.CreatedAt).ToList();
            var sortedWithdrawals = withdrawals.OrderBy(w => w.CreatedAt).ToList();

            // Create a combined list of all outflow events (sells and withdrawals) sorted by date
            var outflowEvents = new List<(DateTime Date, decimal Quantity, string Type)>();

            foreach (var sell in sortedSells)
            {
                outflowEvents.Add((sell.CreatedAt, sell.Quantity ?? 0, "SELL"));
            }

            foreach (var withdrawal in sortedWithdrawals)
            {
                outflowEvents.Add((withdrawal.CreatedAt, withdrawal.Amount, "WITHDRAWAL"));
            }

            // Sort all outflow events by date
            var sortedOutflows = outflowEvents.OrderBy(e => e.Date).ToList();
            var totalOutflowQuantity = sortedOutflows.Sum(e => e.Quantity);

            decimal remainingCostBasis = 0m;
            decimal processedOutflowQuantity = 0m;

            foreach (var buyTransaction in sortedBuys)
            {
                var buyQuantity = buyTransaction.Quantity;
                var buyUnitPrice = buyTransaction.QuoteQuantity / buyQuantity;

                if (processedOutflowQuantity >= totalOutflowQuantity)
                {
                    // All outflows have been accounted for, remaining buys contribute fully to cost basis
                    remainingCostBasis += buyTransaction.QuoteQuantity;
                }
                else if (processedOutflowQuantity + buyQuantity <= totalOutflowQuantity)
                {
                    // This entire buy transaction was consumed by outflows
                    processedOutflowQuantity += buyQuantity ?? 0;
                }
                else
                {
                    // This buy transaction was partially consumed by outflows
                    var remainingFromThisBuy = buyQuantity - (totalOutflowQuantity - processedOutflowQuantity);
                    remainingCostBasis += remainingFromThisBuy * buyUnitPrice ?? 0;
                    processedOutflowQuantity = totalOutflowQuantity;
                }
            }

            return remainingCostBasis;
        }
        #endregion
    }
}