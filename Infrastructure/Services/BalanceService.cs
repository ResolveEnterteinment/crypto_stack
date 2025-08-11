using Application.Interfaces;
using Application.Interfaces.Asset;
using Domain.Constants;
using Domain.Constants.Asset;
using Domain.Constants.Logging;
using Domain.DTOs;
using Domain.DTOs.Balance;
using Domain.DTOs.Logging;
using Domain.Events.Entity;
using Domain.Exceptions;
using Domain.Models.Balance;
using Domain.Models.Transaction;
using Infrastructure.Services.Base;
using MongoDB.Driver;

namespace Infrastructure.Services
{
    public class BalanceService : BaseService<BalanceData>, IBalanceService
    {
        private readonly IAssetService _assetService;

        // Cache keys and durations
        private const string USER_BALANCE_CACHE_KEY = "user_balance:{0}";
        private const string USER_BALANCE_BY_TICKER_CACHE_KEY = "user_balance_ticker:{0}:{1}";
        private const string USER_BALANCES_BY_TYPE_CACHE_KEY = "user_balances_type:{0}:{1}";
        private const string USER_BALANCES_WITH_ASSETS_CACHE_KEY = "user_balances_assets:{0}:{1}";

        private static readonly TimeSpan USER_BALANCE_CACHE_DURATION = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan USER_BALANCES_CACHE_DURATION = TimeSpan.FromMinutes(15);

        public BalanceService(
            IServiceProvider serviceProvider,
            IAssetService assetService
        ) : base(
            serviceProvider,
            new()
            {
                IndexModels = [
                    new CreateIndexModel<BalanceData>(Builders<BalanceData>.IndexKeys.Ascending(b => b.UserId), new CreateIndexOptions { Name = "UserId_1" }),
                    new CreateIndexModel<BalanceData>(Builders<BalanceData>.IndexKeys.Ascending(b => b.AssetId), new CreateIndexOptions { Name = "AssetId_1" })
                    ]
            }
        )
        {
            _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
        }

        public Task<ResultWrapper<List<BalanceData>>> GetAllByUserIdAsync(Guid userId, string? assetType = null)
        {
            return _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Balance",
                    FileName = "BalanceService",
                    OperationName = "GetAllByUserIdAsync(Guid userId, string? assetType = null)",
                    State = {
                        ["UserId"] = userId,
                        ["AssetType"] = assetType,
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    if (userId == Guid.Empty)
                    {
                        throw new ArgumentException("Invalid userId");
                    }

                    // Create cache key based on asset type
                    string cacheKey = string.IsNullOrEmpty(assetType)
                        ? string.Format(USER_BALANCE_CACHE_KEY, userId)
                        : string.Format(USER_BALANCES_BY_TYPE_CACHE_KEY, userId, assetType);

                    var cachedBalances = await _cacheService.GetAnyCachedAsync<List<BalanceData>>(
                        cacheKey,
                        async () =>
                        {
                            var filter = Builders<BalanceData>.Filter.Eq(b => b.UserId, userId);
                            var allWr = await GetManyAsync(filter);
                            if (!allWr.IsSuccess)
                            {
                                throw new Exception(allWr.ErrorMessage);
                            }

                            var balances = allWr.Data ?? Enumerable.Empty<BalanceData>();
                            var filtered = new List<BalanceData>();

                            if (!string.IsNullOrWhiteSpace(assetType) && AssetType.AllValues.Contains(assetType))
                            {
                                foreach (var bal in balances)
                                {
                                    var assetWr = await _assetService.GetByIdAsync(bal.AssetId);
                                    if (assetWr.IsSuccess && assetWr.Data != null &&
                                        assetWr.Data.Type.Equals(assetType, StringComparison.OrdinalIgnoreCase))
                                    {
                                        filtered.Add(bal);
                                    }
                                }
                            }
                            else
                            {
                                filtered = balances.ToList();
                            }

                            return filtered;
                        },
                        USER_BALANCES_CACHE_DURATION);

                    return cachedBalances ?? [];
                })
                .ExecuteAsync();
        }

        public Task<ResultWrapper<BalanceData>> GetUserBalanceByTickerAsync(Guid userId, string ticker)
        {
            return _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Balance",
                    FileName = "BalanceService",
                    OperationName = "GetUserBalanceByTickerAsync(Guid userId, string ticker)",
                    State = {
                        ["UserId"] = userId,
                        ["Ticker"] = ticker,
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    if (userId == Guid.Empty)
                    {
                        throw new ArgumentException("Invalid userId");
                    }

                    var cacheKey = string.Format(USER_BALANCE_BY_TICKER_CACHE_KEY, userId, ticker.ToUpperInvariant());

                    var cachedBalance = await _cacheService.GetAnyCachedAsync<BalanceData>(
                        cacheKey,
                        async () =>
                        {
                            var filter = Builders<BalanceData>.Filter.And([
                                Builders<BalanceData>.Filter.Eq(b => b.UserId, userId),
                                Builders<BalanceData>.Filter.Eq(b => b.Ticker, ticker)]);

                            var balanceResult = await GetOneAsync(filter);
                            if (balanceResult == null || !balanceResult.IsSuccess)
                                throw new DatabaseException("Failed to fetch user balance by ticker");

                            return balanceResult.Data;
                        },
                        USER_BALANCE_CACHE_DURATION);

                    return cachedBalance ?? new BalanceData
                    {
                        UserId = userId,
                        AssetId = Guid.Empty,
                        Ticker = ticker
                    };
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<List<BalanceData>>> FetchBalancesWithAssetsAsync(Guid userId, string? assetType = null)
        {
            if (userId == Guid.Empty)
            {
                return ResultWrapper<List<BalanceData>>.Failure(FailureReason.ValidationError, "Invalid userId");
            }

            if (!string.IsNullOrWhiteSpace(assetType) && !AssetType.AllValues.Contains(assetType.ToUpper()))
            {
                return ResultWrapper<List<BalanceData>>.Failure(FailureReason.ValidationError, "Invalid asset type");
            }

            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Balance",
                    FileName = "BalanceService",
                    OperationName = "FetchBalancesWithAssetsAsync(Guid userId, string? assetType = null)",
                    State = {
                        ["UserId"] = userId,
                        ["AssetType"] = assetType,
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    var cacheKey = string.Format(USER_BALANCES_WITH_ASSETS_CACHE_KEY, userId, assetType ?? "all");

                    var cachedBalances = await _cacheService.GetAnyCachedAsync<List<BalanceData>>(
                        cacheKey,
                        async () =>
                        {
                            FilterDefinition<BalanceData>[] filters = [
                               Builders<BalanceData>.Filter.Eq(b => b.UserId, userId),
                                Builders<BalanceData>.Filter.Gt(b => b.Total, 0m),
                            ];

                            var filter = Builders<BalanceData>.Filter.And(filters);

                            var balancesResult = await GetManyAsync(filter);

                            if (balancesResult == null || !balancesResult.IsSuccess)
                            {
                                throw new DatabaseException($"Failed to fetch user balances: {balancesResult?.ErrorMessage ?? "Fetch result returned null"}");
                            }

                            var balances = balancesResult.Data ?? [];
                            var result = new List<BalanceData>();

                            foreach (var bal in balances)
                            {
                                var assetWr = await _assetService.GetByIdAsync(bal.AssetId);
                                if (assetWr == null || !assetWr.IsSuccess)
                                {
                                    await _loggingService.LogTraceAsync($"Asset not found for ID {bal.AssetId}", level: LogLevel.Error);
                                    continue;
                                }

                                if (!string.IsNullOrWhiteSpace(assetType) && assetWr.Data.Type != assetType.ToUpper()) continue;

                                result.Add(new BalanceData
                                {
                                    Id = bal.Id,
                                    UserId = bal.UserId,
                                    AssetId = bal.AssetId,
                                    Ticker = bal.Ticker,
                                    Available = bal.Available,
                                    Locked = bal.Locked,
                                    Total = bal.Total,
                                    Asset = assetWr.Data,
                                    UpdatedAt = bal.UpdatedAt
                                });
                            }

                            return result;
                        },
                        USER_BALANCES_CACHE_DURATION);

                    return cachedBalances ?? [];
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<BalanceData>> UpsertBalanceAsync(Guid userId, BalanceUpdateDto balanceUpdateDto, IClientSessionHandle? session = null)
        {
            if (userId == Guid.Empty)
            {
                return ResultWrapper<BalanceData>.Failure(FailureReason.ValidationError, "Invalid userId");
            }

            if (balanceUpdateDto == null || balanceUpdateDto.AssetId == Guid.Empty || balanceUpdateDto.LastTransactionId == Guid.Empty)
            {
                return ResultWrapper<BalanceData>.Failure(FailureReason.ValidationError, "Invalid balance data");
            }

            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Balance",
                    FileName = "BalanceService",
                    OperationName = "UpsertBalanceAsync(Guid userId, BalanceData updateBalance, IClientSessionHandle? session = null)",
                    State = {
                        ["UserId"] = userId,
                        ["Session"] = session,
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    var filter = Builders<BalanceData>.Filter.And(
                        Builders<BalanceData>.Filter.Eq(b => b.UserId, userId),
                        Builders<BalanceData>.Filter.Eq(b => b.AssetId, balanceUpdateDto.AssetId)
                    );

                    var existingResult = await GetByIdAsync(balanceUpdateDto.AssetId);

                    BalanceData resultBalance;

                    if (existingResult != null && existingResult.IsSuccess && existingResult.Data != null)
                    {
                        var existing = existingResult.Data;

                        var available = existing.Available + balanceUpdateDto.Available;
                        var locked = existing.Locked + balanceUpdateDto.Locked;
                        var total = available + locked;

                        var fields = new Dictionary<string, object>
                        {
                            ["Available"] = available,
                            ["Locked"] = locked,
                            ["Total"] = total,
                            ["UpdatedAt"] = balanceUpdateDto.LastUpdated,
                            ["LastTransactionId"] = balanceUpdateDto.LastTransactionId
                        };

                        var updateWr = await UpdateAsync(existing.Id, fields);

                        if (updateWr == null || !updateWr.IsSuccess || !updateWr.Data.IsSuccess)
                        {
                            throw new DatabaseException($"Failed to update balance: {updateWr?.ErrorMessage ?? "Update result returned null"}");
                        }

                        resultBalance = updateWr.Data.Documents.First();
                    }
                    else
                    {
                        var assetResult = await _assetService.GetByIdAsync(balanceUpdateDto.AssetId);
                        if (assetResult == null || !assetResult.IsSuccess || assetResult.Data == null)
                        {
                            throw new ResourceNotFoundException("Asset", balanceUpdateDto.AssetId.ToString());
                        }

                        var asset = assetResult.Data;

                        resultBalance = new BalanceData
                        {
                            UserId = userId,
                            AssetId = asset.Id,
                            Ticker = asset.Ticker,
                            Available = balanceUpdateDto.Available,
                            Locked = balanceUpdateDto.Locked,
                            Total = balanceUpdateDto.Available + balanceUpdateDto.Locked,
                            LastTransactionId = balanceUpdateDto.LastTransactionId,
                            UpdatedAt = balanceUpdateDto.LastUpdated
                        };

                        var insertWr = await InsertAsync(resultBalance);
                        if (insertWr == null || !insertWr.IsSuccess || !insertWr.Data.IsSuccess)
                        {
                            throw new DatabaseException($"Failed to insert balance: {insertWr?.ErrorMessage ?? "Insert result returned null"}");
                        }
                    }

                    // Invalidate and update related caches after successful upsert
                    await InvalidateAndUpdateUserCachesAsync(userId, resultBalance.Ticker, resultBalance);

                    return resultBalance;
                })
                .ExecuteAsync();
        }

        public async Task Handle(EntityCreatedEvent<TransactionData> notification, CancellationToken cancellationToken)
        {
            await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Balance",
                    FileName = "BalanceService",
                    OperationName = "Handle(EntityCreatedEvent<TransactionData> notification, CancellationToken cancellationToken)",
                    State = {
                        ["TransactionId"] = notification.Entity.Id,
                        ["UserId"] = notification.Entity.UserId,
                        ["SubscriptionId"] = notification.Entity.SubscriptionId,
                        ["AssetId"] = notification.Entity.AssetId,
                        ["SourceName"] = notification.Entity.SourceName,
                        ["Action"] = notification.Entity.Action,
                        ["Amount"] = notification.Entity.Quantity.ToString(),
                        ["CancellationToken"] = cancellationToken,
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    var transaction = notification.Entity;
                    var assetId = notification.Entity.AssetId;

                    // Invalidate user balance caches before processing
                    await InvalidateUserBalanceCachesAsync(transaction.UserId);

                    // Create balance update data based on transaction action
                    var balanceUpdateDto = BalanceUpdateDto.FromTransaction(transaction);

                    var upsertResult = await UpsertBalanceAsync(transaction.UserId, balanceUpdateDto);

                    if (upsertResult == null || !upsertResult.IsSuccess)
                    {
                        throw new DatabaseException($"Failed to upsert balance for user {transaction.UserId}: {upsertResult?.ErrorMessage ?? "Upsert result returned null"}");
                    }

                    _loggingService.LogInformation("Balance updated for user {UserId} due to transaction {TransactionId}",
                        transaction.UserId, transaction.Id);
                })
                .ExecuteAsync();
        }

        /// <summary>
        /// Invalidates all balance-related caches for a specific user
        /// </summary>
        /// <param name="userId">The user ID to invalidate caches for</param>
        private async Task InvalidateUserBalanceCachesAsync(Guid userId)
        {
            try
            {
                // Invalidate all user-related balance cache keys
                var cacheKeysToInvalidate = new[]
                {
                    string.Format(USER_BALANCE_CACHE_KEY, userId),
                    string.Format(USER_BALANCES_WITH_ASSETS_CACHE_KEY, userId, "all")
                };

                // Invalidate typed asset balance caches
                var commonAssetTypes = new[] { "Exchange", "Staking", "DeFi", "CRYPTO", "STABLECOIN" };
                var typedCacheKeys = commonAssetTypes.Select(assetType =>
                    string.Format(USER_BALANCES_BY_TYPE_CACHE_KEY, userId, assetType))
                    .Concat(commonAssetTypes.Select(assetType =>
                        string.Format(USER_BALANCES_WITH_ASSETS_CACHE_KEY, userId, assetType)));

                foreach (var key in cacheKeysToInvalidate.Concat(typedCacheKeys))
                {
                    _cacheService.Invalidate(key);
                }

                _loggingService.LogInformation("Invalidated balance caches for user {UserId}", userId);
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Failed to invalidate balance caches for user {UserId}: {Error}", userId, ex.Message);
            }
        }

        /// <summary>
        /// Invalidates user balance caches and optionally updates specific ticker cache
        /// </summary>
        /// <param name="userId">The user ID</param>
        /// <param name="ticker">The asset ticker (optional)</param>
        /// <param name="updatedBalance">The updated balance data (optional)</param>
        private async Task InvalidateAndUpdateUserCachesAsync(Guid userId, string? ticker, BalanceData? updatedBalance = null)
        {
            try
            {
                // First invalidate all user caches
                await InvalidateUserBalanceCachesAsync(userId);

                // If we have updated balance data, cache the specific ticker balance
                if (!string.IsNullOrEmpty(ticker) && updatedBalance != null)
                {
                    var tickerCacheKey = string.Format(USER_BALANCE_BY_TICKER_CACHE_KEY, userId, ticker.ToUpperInvariant());
                    _cacheService.Set(tickerCacheKey, updatedBalance, USER_BALANCE_CACHE_DURATION);

                    _loggingService.LogInformation("Updated ticker cache for user {UserId}, ticker {Ticker}", userId, ticker);
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Failed to invalidate and update caches for user {UserId}: {Error}", userId, ex.Message);
            }
        }

        /// <summary>
        /// Invalidates balance caches for multiple users efficiently
        /// </summary>
        /// <param name="userIds">Collection of user IDs to invalidate</param>
        public async Task InvalidateBalanceCachesForUsersAsync(IEnumerable<Guid> userIds)
        {
            if (userIds == null || !userIds.Any())
                return;

            try
            {
                var tasks = userIds.Select(InvalidateUserBalanceCachesAsync);
                await Task.WhenAll(tasks);

                _loggingService.LogInformation("Invalidated balance caches for {UserCount} users", userIds.Count());
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Failed to invalidate balance caches for multiple users: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Warms up the cache for a specific user's essential balances
        /// </summary>
        /// <param name="userId">The user ID to warm up cache for</param>
        public async Task<ResultWrapper> WarmupUserBalanceCacheAsync(Guid userId)
        {
            try
            {
                _loggingService.LogInformation("Warming up balance cache for user {UserId}", userId);

                // Pre-load user balances
                var balancesTask = GetAllByUserIdAsync(userId);
                var balancesWithAssetsTask = FetchBalancesWithAssetsAsync(userId);

                await Task.WhenAll(balancesTask, balancesWithAssetsTask);

                if (balancesTask.Result.IsSuccess && balancesWithAssetsTask.Result.IsSuccess)
                {
                    _loggingService.LogInformation("Successfully warmed up balance cache for user {UserId}", userId);
                    return ResultWrapper.Success("Balance cache warmed up successfully");
                }
                else
                {
                    var error = balancesTask.Result.ErrorMessage ?? balancesWithAssetsTask.Result.ErrorMessage;
                    _loggingService.LogWarning("Failed to warm up balance cache for user {UserId}: {Error}", userId, error);
                    return ResultWrapper.Failure(
                        FailureReason.CacheOperationFailed,
                        $"Failed to warm up cache: {error}");
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Error warming up balance cache for user {UserId}: {Error}", userId, ex.Message);
                return ResultWrapper.FromException(ex);
            }
        }

        /// <summary>
        /// Gets cache statistics for monitoring balance cache health
        /// </summary>
        /// <param name="userId">The user ID to get stats for</param>
        public async Task<ResultWrapper<BalanceCacheStats>> GetCacheStatsAsync(Guid userId)
        {
            try
            {
                var stats = new BalanceCacheStats
                {
                    UserId = userId,
                    UserBalanceExists = _cacheService.TryGetValue<List<BalanceData>>(string.Format(USER_BALANCE_CACHE_KEY, userId), out _),
                    BalancesWithAssetsExists = _cacheService.TryGetValue<List<BalanceData>>(string.Format(USER_BALANCES_WITH_ASSETS_CACHE_KEY, userId, "all"), out _),
                    Timestamp = DateTime.UtcNow
                };

                // Check some common ticker caches
                var commonTickers = new[] { "BTC", "USDT", "ETH" };
                stats.CommonTickerCacheHits = 0;
                foreach (var ticker in commonTickers)
                {
                    var cacheKey = string.Format(USER_BALANCE_BY_TICKER_CACHE_KEY, userId, ticker);
                    if (_cacheService.TryGetValue<BalanceData>(cacheKey, out _))
                    {
                        stats.CommonTickerCacheHits++;
                    }
                }

                return ResultWrapper<BalanceCacheStats>.Success(stats);
            }
            catch (Exception ex)
            {
                return ResultWrapper<BalanceCacheStats>.FromException(ex);
            }
        }
    }

    /// <summary>
    /// Cache statistics for monitoring balance cache health
    /// </summary>
    public class BalanceCacheStats
    {
        public Guid UserId { get; set; }
        public bool UserBalanceExists { get; set; }
        public bool BalancesWithAssetsExists { get; set; }
        public int CommonTickerCacheHits { get; set; }
        public DateTime Timestamp { get; set; }
    }
}