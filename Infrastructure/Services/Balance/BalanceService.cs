using Application.Interfaces;
using Application.Interfaces.Asset;
using CryptoExchange.Net.CommonObjects;
using Domain.Constants;
using Domain.Constants.Asset;
using Domain.Constants.Logging;
using Domain.DTOs;
using Domain.DTOs.Balance;
using Domain.DTOs.Logging;
using Domain.DTOs.Transaction;
using Domain.Events.Entity;
using Domain.Exceptions;
using Domain.Models.Balance;
using Domain.Models.Transaction;
using Infrastructure.Services.Base;
using MongoDB.Driver;

namespace Infrastructure.Services.Balance
{
    public class BalanceService : BaseService<BalanceData>, IBalanceService
    {
        private readonly IAssetService _assetService;

        // Cache keys and durations
        private const string USER_BALANCE_CACHE_KEY = "user_balance:{0}";
        private const string USER_BALANCE_BY_TICKER_CACHE_KEY = "user_balance_ticker:{0}:{1}";
        private const string USER_ENHANCED_BALANCE_BY_TICKER_CACHE_KEY = "user_enhanced_balance_ticker:{0}:{1}";
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

                    var cachedBalances = await _cacheService.GetAnyCachedAsync(
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
            ticker = ticker.ToUpperInvariant();

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

                    var cachedBalance = await _cacheService.GetAnyCachedAsync(
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

                    var cachedBalances = await _cacheService.GetAnyCachedAsync(
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

        /// <summary>
        /// Gets or creates a balance enhanced with asset info.
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="ticker"></param>
        /// <returns></returns>
        /// <exception cref="DatabaseException"></exception>
        /// <exception cref="AssetFetchException"></exception>
        public async Task<ResultWrapper<BalanceData>> GetOrCreateEnhancedBalanceAsync(Guid userId, string ticker)
        {
            if (userId == Guid.Empty)
            {
                return ResultWrapper<BalanceData>.Failure(FailureReason.ValidationError, "Invalid userId");
            }
            
            if (string.IsNullOrWhiteSpace(ticker))
            {
                return ResultWrapper<BalanceData>.Failure(FailureReason.ValidationError, "Invalid asset ticker");
            }

            ticker = ticker.ToUpperInvariant();

            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Balance",
                    FileName = "BalanceService",
                    OperationName = "GetOrCreateEnhancedBalanceAsync(Guid userId, string ticker)",
                    State = {
                        ["UserId"] = userId,
                        ["AssetTicker"] = ticker,
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    var cacheKey = string.Format(USER_ENHANCED_BALANCE_BY_TICKER_CACHE_KEY, userId, ticker);

                    var cachedBalance = await _cacheService.GetAnyCachedAsync(
                        cacheKey,
                        async () =>
                        {
                            var balance = await GetOrCreateOneAsync(userId, ticker);

                            var assetWr = await _assetService.GetByTickerAsync(ticker);
                            if (assetWr == null || !assetWr.IsSuccess)
                            {
                                await _loggingService.LogTraceAsync($"Asset not found for ID {balance.AssetId}", level: LogLevel.Error);
                                throw new AssetFetchException($"Asset not found for ID {balance.AssetId}");
                            }

                            return new BalanceData
                            {
                                Id = balance.Id,
                                UserId = balance.UserId,
                                AssetId = balance.AssetId,
                                Ticker = balance.Ticker,
                                Available = balance.Available,
                                Locked = balance.Locked,
                                Total = balance.Total,
                                Asset = assetWr.Data,
                                UpdatedAt = balance.UpdatedAt
                            };
                        },
                        USER_BALANCES_CACHE_DURATION);

                    return cachedBalance ?? new BalanceData
                    {
                        UserId = userId,
                        AssetId = Guid.Empty,
                        Ticker = ticker
                    };
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

                    var existingResult = await GetOneAsync(filter);

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

        // ===== UPDATED HANDLE METHOD FOR BALANCESERVICE =====
        // Replace the existing Handle method in BalanceService with this version
        // This properly processes double-entry transactions with FromBalance, ToBalance, Fee, and Rounding

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
                ["Action"] = notification.Entity.Action,
                ["CancellationToken"] = cancellationToken,
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    var transaction = notification.Entity;

                    // Get all affected user IDs for cache invalidation
                    var affectedUserIds = transaction.GetAffectedUserIds();

                    // Invalidate user balance caches before processing
                    foreach (var userId in affectedUserIds)
                    {
                        await InvalidateUserBalanceCachesAsync(userId);
                    }

                    // Process all balance entries in the transaction
                    var updateTasks = new List<Task<ResultWrapper<BalanceData>>>();

                    // Process FromBalance (debit)
                    if (transaction.FromBalance != null)
                    {
                        updateTasks.Add(ProcessBalanceEntryAsync(
                            transaction.FromBalance,
                            transaction.Id,
                            isDebit: true));
                    }

                    // Process ToBalance (credit)
                    if (transaction.ToBalance != null)
                    {
                        updateTasks.Add(ProcessBalanceEntryAsync(
                            transaction.ToBalance,
                            transaction.Id,
                            isDebit: false));
                    }

                    // Process Fee
                    if (transaction.Fee != null)
                    {
                        updateTasks.Add(ProcessBalanceEntryAsync(
                            transaction.Fee,
                            transaction.Id,
                            isDebit: false)); // Fee is typically a credit to the recipient
                    }

                    // Process Rounding
                    if (transaction.Rounding != null)
                    {
                        updateTasks.Add(ProcessBalanceEntryAsync(
                            transaction.Rounding,
                            transaction.Id,
                            isDebit: transaction.Rounding.Quantity < 0));
                    }

                    // Execute all balance updates
                    var results = await Task.WhenAll(updateTasks);

                    // Check if any updates failed
                    var failedUpdates = results.Where(r => r == null || !r.IsSuccess).ToList();
                    if (failedUpdates.Any())
                    {
                        var errorMessages = string.Join(", ", failedUpdates.Select(r => r?.ErrorMessage ?? "Unknown error"));
                        throw new DatabaseException($"Failed to update balances for transaction {transaction.Id}: {errorMessages}");
                    }

                    _loggingService.LogInformation(
                        "Balance updated for transaction {TransactionId}, affected {UserCount} users, {UpdateCount} balance updates",
                        transaction.Id, affectedUserIds.Count(), updateTasks.Count);
                })
                .ExecuteAsync();
        }

        /// <summary>
        /// Processes a single balance entry (FromBalance, ToBalance, Fee, or Rounding)
        /// </summary>
        private async Task<ResultWrapper<BalanceData>> ProcessBalanceEntryAsync(
            TransactionEntry entry,
            Guid transactionId,
            bool isDebit)
        {
            try
            {
                // Create BalanceUpdateDto from the entry
                var updateDto = new BalanceUpdateDto
                {
                    AssetId = entry.AssetId,
                    Available = 0,
                    Locked = 0,
                    LastTransactionId = transactionId,
                    LastUpdated = DateTime.UtcNow
                };

                // Apply the balance change based on BalanceType
                switch (entry.BalanceType)
                {
                    case BalanceType.Available:
                        updateDto.Available = entry.Quantity;
                        break;

                    case BalanceType.Locked:
                        updateDto.Locked = entry.Quantity;
                        break;

                    case BalanceType.LockFromAvailable:
                        // Move from Available to Locked
                        updateDto.Available = -Math.Abs(entry.Quantity);
                        updateDto.Locked = Math.Abs(entry.Quantity);
                        break;

                    case BalanceType.UnlockToAvailable:
                        // Move from Locked to Available
                        updateDto.Locked = -Math.Abs(entry.Quantity);
                        updateDto.Available = Math.Abs(entry.Quantity);
                        break;

                    default:
                        throw new InvalidOperationException($"Unsupported BalanceType: {entry.BalanceType}");
                }

                // Update the balance
                var result = await UpsertBalanceAsync(entry.UserId, updateDto);

                if (result.IsSuccess && result.Data != null)
                {
                    _loggingService.LogInformation(
                        "Updated balance for user {UserId}, asset {AssetId}: Available {Available}, Locked {Locked}",
                        entry.UserId, entry.AssetId, updateDto.Available, updateDto.Locked);
                }

                return result;
            }
            catch (Exception ex)
            {
                _loggingService.LogError(
                    "Failed to process balance entry for user {UserId}, asset {AssetId}: {Error}",
                    entry.UserId, entry.AssetId, ex.Message);

                return ResultWrapper<BalanceData>.Failure(
                    FailureReason.DatabaseError,
                    $"Failed to process balance entry: {ex.Message}");
            }
        }

        // ===== ALTERNATIVE: ENHANCED UPSERT METHOD =====
        // If you want better validation and snapshots, you can use this enhanced version instead

        /// <summary>
        /// Enhanced version that includes balance validation and snapshot recording
        /// </summary>
        private async Task<ResultWrapper<BalanceData>> ProcessBalanceEntryWithValidationAsync(
            TransactionEntry entry,
            Guid transactionId,
            bool isDebit)
        {
            try
            {
                // Get existing balance or create new one
                var filter = Builders<BalanceData>.Filter.And(
                    Builders<BalanceData>.Filter.Eq(b => b.UserId, entry.UserId),
                    Builders<BalanceData>.Filter.Eq(b => b.AssetId, entry.AssetId)
                );

                var existingResult = await GetOneAsync(filter);
                BalanceData balance;

                if (existingResult != null && existingResult.IsSuccess && existingResult.Data != null)
                {
                    balance = existingResult.Data;
                }
                else
                {
                    // Create new balance
                    var assetResult = await _assetService.GetByIdAsync(entry.AssetId);
                    if (assetResult == null || !assetResult.IsSuccess || assetResult.Data == null)
                    {
                        throw new ResourceNotFoundException("Asset", entry.AssetId.ToString());
                    }

                    balance = new BalanceData
                    {
                        UserId = entry.UserId,
                        AssetId = entry.AssetId,
                        Ticker = entry.Ticker ?? assetResult.Data.Ticker,
                        Available = 0,
                        Locked = 0,
                        Total = 0,
                        LastTransactionId = transactionId,
                        TransactionCount = 0
                    };
                }

                // Record BEFORE snapshot
                entry.BalanceBeforeAvailable = balance.Available;
                entry.BalanceBeforeLocked = balance.Locked;
                entry.BalanceId = balance.Id;

                // Apply the balance change based on BalanceType
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

                    default:
                        throw new InvalidOperationException($"Unsupported BalanceType: {entry.BalanceType}");
                }

                // CRITICAL: Validate balance constraints
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

                // Update totals
                balance.Total = balance.Available + balance.Locked;
                balance.LastTransactionId = transactionId;
                balance.LastTransactionAt = DateTime.UtcNow;
                balance.TransactionCount++;
                balance.UpdatedAt = DateTime.UtcNow;

                // Record AFTER snapshot
                entry.BalanceAfterAvailable = balance.Available;
                entry.BalanceAfterLocked = balance.Locked;

                // Save the balance
                ResultWrapper<BalanceData> result;
                if (balance.Id == Guid.Empty)
                {
                    var insertResult = await InsertAsync(balance);
                    if(insertResult == null || !insertResult.IsSuccess || !insertResult.Data.IsSuccess)
                    {
                        throw new DatabaseException($"Failed to insert balance: {insertResult?.ErrorMessage}");
                    }
                    result = ResultWrapper<BalanceData>.Success(insertResult.Data.Documents.First());
                }
                else
                {
                    var fields = new Dictionary<string, object>
                    {
                        ["Available"] = balance.Available,
                        ["Locked"] = balance.Locked,
                        ["Total"] = balance.Total,
                        ["LastTransactionId"] = balance.LastTransactionId,
                        ["LastTransactionAt"] = balance.LastTransactionAt,
                        ["TransactionCount"] = balance.TransactionCount,
                        ["UpdatedAt"] = balance.UpdatedAt
                    };

                    var updateResult = await UpdateAsync(balance.Id, fields);

                    if (updateResult == null || !updateResult.IsSuccess || !updateResult.Data.IsSuccess)
                    {
                        throw new DatabaseException($"Failed to update balance: {updateResult?.ErrorMessage}");
                    }

                    result = ResultWrapper<BalanceData>.Success(updateResult.Data.Documents.First());
                }

                if (result.IsSuccess)
                {
                    _loggingService.LogInformation(
                        "Updated balance {BalanceId} for user {UserId}, asset {AssetId}: {Before} → {After}",
                        balance.Id, entry.UserId, entry.AssetId,
                        entry.BalanceBeforeAvailable, entry.BalanceAfterAvailable);
                }

                return result;
            }
            catch (Exception ex)
            {
                _loggingService.LogError(
                    "Failed to process balance entry for user {UserId}, asset {AssetId}: {Error}",
                    entry.UserId, entry.AssetId, ex.Message);

                return ResultWrapper<BalanceData>.Failure(
                    FailureReason.DatabaseError,
                    $"Failed to process balance entry: {ex.Message}");
            }
        }

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

        private async Task<BalanceData> GetOrCreateOneAsync(Guid userId, string ticker)
        {
            ticker = ticker.ToUpperInvariant();

            // First try to get the balance
            FilterDefinition<BalanceData>[] filters = [
                                Builders<BalanceData>.Filter.Eq(b => b.UserId, userId),
                                Builders<BalanceData>.Filter.Eq(b => b.Ticker, ticker.ToUpper()),
                                Builders<BalanceData>.Filter.Gt(b => b.Total, 0m),
                            ];

            var filter = Builders<BalanceData>.Filter.And(filters);

            var balanceResult = await GetOneAsync(filter);

            if(balanceResult.IsSuccess && balanceResult.Data != null)
            {
                return balanceResult.Data;
            }

            // If no balance is found, then create a new one and return it.

            var assetResult = await _assetService.GetByTickerAsync(ticker);

            if(!assetResult.IsSuccess || assetResult.Data == null)
            {
                throw new AssetFetchException(assetResult.ErrorMessage);
            }

            var asset = assetResult.Data;

            var balanceEntity = new BalanceData
            {
                UserId = userId,
                AssetId = asset.Id,
                Ticker = ticker,
                Available = 0m,
                Locked = 0m,
                Total = 0m
            };

            var newBalanceResult = await InsertAsync(balanceEntity);

            if(!newBalanceResult.IsSuccess || !newBalanceResult.Data.IsSuccess)
            {
                throw new DatabaseException(newBalanceResult.Data.ErrorMessage);
            }

            var enhancedEntity = newBalanceResult.Data.Documents.First();

            enhancedEntity.Asset = asset;

            return enhancedEntity;
        }
    }
}