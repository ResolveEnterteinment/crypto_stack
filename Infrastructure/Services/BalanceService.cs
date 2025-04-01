using Application.Interfaces;
using Domain.Constants;
using Domain.DTOs;
using Domain.DTOs.Balance;
using Domain.DTOs.Settings;
using Domain.Events;
using Domain.Models.Balance;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using System.Diagnostics;

namespace Infrastructure.Services
{
    public class BalanceService : BaseService<BalanceData>, IBalanceService, INotificationHandler<PaymentReceivedEvent>
    {
        private readonly IAssetService _assetService;
        private const string CACHE_KEY_USER_BALANCES = "user_balances:{0}:{1}"; // userId_assetClass

        public BalanceService(
            IAssetService assetService,
            IOptions<MongoDbSettings> mongoDbSettings,
            IMongoClient mongoClient,
            IMemoryCache cache,
            ILogger<BalanceService> logger)
            : base(
                  mongoClient,
                  mongoDbSettings,
                  "balances",
                  logger,
                  cache,
                  new List<CreateIndexModel<BalanceData>>
                  {
                      new CreateIndexModel<BalanceData>(
                          Builders<BalanceData>.IndexKeys.Ascending(x => x.UserId),
                          new CreateIndexOptions { Name = "UserId_1" }
                      ),
                      new CreateIndexModel<BalanceData>(
                          Builders<BalanceData>.IndexKeys.Ascending(x => x.AssetId),
                          new CreateIndexOptions { Name = "AssetId_1" }
                      )
                  }
                  )
        {
            _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
        }

        public async Task<ResultWrapper<IEnumerable<BalanceData>>> GetAllByUserIdAsync(Guid userId, string? assetType = null)
        {
            using var activity = new Activity("BalanceService.GetAllByUserIdAsync").Start();
            activity?.SetTag("userId", userId);
            activity?.SetTag("assetClass", assetType ?? "all");

            try
            {
                string cacheKey = string.Format(CACHE_KEY_USER_BALANCES, userId, assetType ?? "all");

                return await GetOrCreateCachedItemAsync<ResultWrapper<IEnumerable<BalanceData>>>(
                    cacheKey,
                    async () =>
                    {
                        _logger.LogInformation("Fetching balances for user {UserId} with assetClass {AssetClass}",
                            userId, assetType ?? "all");

                        var filter = Builders<BalanceData>.Filter.Eq(b => b.UserId, userId);
                        var balances = await GetAllAsync(filter);

                        if (balances is null)
                        {
                            throw new ArgumentNullException(nameof(balances),
                                $"Failed to retrieve balances for user {userId}");
                        }

                        // Filter by asset class if specified
                        var returnBalances = new List<BalanceData>();
                        if (assetType != null && AssetType.AllValues.Contains(assetType))
                        {
                            foreach (var balance in balances)
                            {
                                var assetData = await _assetService.GetByIdAsync(balance.AssetId);
                                if (assetData != null && assetData.Type.ToLowerInvariant() == assetType.ToLowerInvariant())
                                {
                                    returnBalances.Add(balance);
                                }
                            }
                        }
                        else
                        {
                            returnBalances = balances.ToList();
                        }

                        _logger.LogInformation("Retrieved {Count} balances for user {UserId} with assetClass {AssetClass}",
                            returnBalances.Count, userId, assetType ?? "all");

                        return ResultWrapper<IEnumerable<BalanceData>>.Success(returnBalances);
                    },
                    TimeSpan.FromMinutes(5)
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting balances for user {UserId} with assetClass {AssetClass}: {Message}",
                    userId, assetType ?? "all", ex.Message);
                return ResultWrapper<IEnumerable<BalanceData>>.FromException(ex);
            }
        }

        public async Task<List<BalanceDto>> FetchBalancesWithAssetsAsync(Guid userId)
        {
            using var activity = new Activity("BalanceService.FetchBalancesWithAssetsAsync").Start();
            activity?.SetTag("userId", userId);

            string cacheKey = $"balances_with_assets:{userId}";

            return await GetOrCreateCachedItemAsync<List<BalanceDto>>(
                cacheKey,
                async () =>
                {
                    try
                    {
                        // Use simpler approach with multiple queries instead of aggregation
                        var rawBalances = await GetAllAsync(Builders<BalanceData>.Filter.Eq(b => b.UserId, userId));

                        _logger.LogInformation("Found {Count} raw balances for user {UserId}",
                            rawBalances.Count, userId);

                        // If there are no balances, return an empty list
                        if (!rawBalances.Any())
                        {
                            _logger.LogInformation("No balances found for user {UserId}", userId);
                            return new List<BalanceDto>();
                        }

                        var result = new List<BalanceDto>();

                        foreach (var balance in rawBalances)
                        {
                            try
                            {
                                // Get the asset for this balance
                                var asset = await _assetService.GetByIdAsync(balance.AssetId);

                                if (asset == null)
                                {
                                    _logger.LogWarning("Asset not found for ID {AssetId}, user {UserId}",
                                        balance.AssetId, userId);
                                    continue;
                                }

                                // Create the DTO
                                var balanceDto = new BalanceDto
                                {
                                    AssetId = asset.Id.ToString(),
                                    AssetName = asset.Name,
                                    Ticker = asset.Ticker,
                                    Symbol = asset.Symbol,
                                    Available = balance.Available,
                                    Locked = balance.Locked,
                                    Total = balance.Total,
                                    AssetDocs = asset
                                };

                                result.Add(balanceDto);

                                _logger.LogDebug("Successfully mapped balance for asset {Ticker}", asset.Ticker);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error processing balance for asset {AssetId}", balance.AssetId);
                                // Continue with the next balance instead of failing completely
                            }
                        }

                        _logger.LogInformation("Returning {Count} balance DTOs for user {UserId}",
                            result.Count, userId);

                        return result;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to fetch balances with assets for user {UserId}: {ErrorMessage}",
                            userId, ex.Message);
                        throw;
                    }
                },
                TimeSpan.FromMinutes(5)
            );
        }

        public async Task<ResultWrapper<BalanceData>> UpsertBalanceAsync(Guid userId, BalanceData updateBalance, IClientSessionHandle? session = null)
        {
            using var activity = new Activity("BalanceService.UpsertBalanceAsync").Start();
            activity?.SetTag("userId", userId);
            activity?.SetTag("assetId", updateBalance.AssetId);

            try
            {
                // Validate input
                if (updateBalance == null)
                {
                    throw new ArgumentNullException(nameof(updateBalance));
                }

                if (userId == Guid.Empty)
                {
                    throw new ArgumentException("UserId cannot be empty", nameof(userId));
                }

                if (updateBalance.AssetId == Guid.Empty)
                {
                    throw new ArgumentException("AssetId cannot be empty", nameof(updateBalance.AssetId));
                }

                // Check if asset exists
                var asset = await _assetService.GetByIdAsync(updateBalance.AssetId);
                if (asset == null)
                {
                    throw new KeyNotFoundException($"Asset with ID {updateBalance.AssetId} not found");
                }

                _logger.LogInformation("Updating balance for user {UserId}, asset {AssetId}, available {Available}, locked {Locked}",
                    userId, updateBalance.AssetId, updateBalance.Available, updateBalance.Locked);

                // Create filter to find the balance record
                var filter = Builders<BalanceData>.Filter.And(
                    Builders<BalanceData>.Filter.Eq(s => s.UserId, userId),
                    Builders<BalanceData>.Filter.Eq(s => s.AssetId, updateBalance.AssetId)
                );

                // Create update definition
                var update = Builders<BalanceData>.Update
                    .Inc(s => s.Available, updateBalance.Available)
                    .Inc(s => s.Locked, updateBalance.Locked)
                    .Inc(s => s.Total, updateBalance.Available + updateBalance.Locked)
                    .Set(s => s.LastUpdated, DateTime.UtcNow);

                // If ticker is provided, use it (useful for first-time inserts)
                if (!string.IsNullOrEmpty(updateBalance.Ticker))
                {
                    update = update.Set(s => s.Ticker, updateBalance.Ticker);
                }

                // Execute the update with upsert
                FindOneAndUpdateOptions<BalanceData> options = new()
                {
                    IsUpsert = true,
                    ReturnDocument = ReturnDocument.After
                };

                BalanceData updatedBalance;
                if (session != null)
                {
                    updatedBalance = await _collection.FindOneAndUpdateAsync(session, filter, update, options);
                }
                else
                {
                    updatedBalance = await _collection.FindOneAndUpdateAsync(filter, update, options);
                }

                if (updatedBalance == null)
                {
                    throw new InvalidOperationException("Failed to update or insert balance record");
                }

                // Invalidate cache
                InvalidateUserBalanceCache(userId);

                _logger.LogInformation("Successfully updated balance for user {UserId}, asset {AssetId}. " +
                    "New values: Available: {Available}, Locked: {Locked}, Total: {Total}",
                    userId, updateBalance.AssetId, updatedBalance.Available, updatedBalance.Locked, updatedBalance.Total);

                return ResultWrapper<BalanceData>.Success(updatedBalance);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating balance for user {UserId}, asset {AssetId}: {Message}",
                    userId, updateBalance.AssetId, ex.Message);
                return ResultWrapper<BalanceData>.FromException(ex);
            }
        }

        public async Task Handle(PaymentReceivedEvent notification, CancellationToken cancellationToken)
        {
            using var activity = new Activity("BalanceService.HandlePaymentReceivedEvent").Start();
            activity?.SetTag("paymentId", notification.Payment.Id);
            activity?.SetTag("userId", notification.Payment.UserId);
            activity?.SetTag("subscriptionId", notification.Payment.SubscriptionId);

            try
            {
                _logger.LogInformation("Processing payment received event for payment {PaymentId}, user {UserId}",
                    notification.Payment.Id, notification.Payment.UserId);

                var payment = notification.Payment;

                // Get the asset for the payment currency
                var assetResult = await _assetService.GetByTickerAsync(payment.Currency);
                if (assetResult is null || !assetResult.IsSuccess || assetResult.Data is null)
                {
                    throw new Exception($"Failed to retrieve asset data for ticker {payment.Currency}: " +
                        $"{assetResult?.ErrorMessage ?? "Asset result returned null."}");
                }

                // Update the user's balance for this currency
                await UpsertBalanceAsync(payment.UserId, new()
                {
                    UserId = payment.UserId,
                    AssetId = assetResult.Data.Id,
                    Ticker = assetResult.Data.Ticker,
                    Available = payment.NetAmount,
                    Locked = 0
                });

                _logger.LogInformation("Successfully processed payment received event for payment {PaymentId}. " +
                    "Updated balance for asset {Ticker}", notification.Payment.Id, payment.Currency);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to handle payment received event for payment {PaymentId}: {Message}",
                    notification.Payment.Id, ex.Message);
                // Don't rethrow - we don't want to disrupt the event processing pipeline
            }
        }

        // Helper method to invalidate all cache entries for a user's balances
        private void InvalidateUserBalanceCache(Guid userId)
        {
            // Invalidate specific user balance caches
            foreach (var assetClass in AssetType.AllValues)
            {
                _cache.Remove(string.Format(CACHE_KEY_USER_BALANCES, userId, assetClass));
            }

            // Invalidate the "all" assets cache
            _cache.Remove(string.Format(CACHE_KEY_USER_BALANCES, userId, "all"));

            // Invalidate any other balance-related caches
            _cache.Remove($"balances_with_assets:{userId}");

            _logger.LogDebug("Invalidated balance caches for user {UserId}", userId);
        }
    }
}