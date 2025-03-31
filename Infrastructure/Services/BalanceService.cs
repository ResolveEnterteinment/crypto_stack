using Application.Interfaces;
using Domain.Constants;
using Domain.DTOs;
using Domain.DTOs.Settings;
using Domain.Events;
using Domain.Models.Balance;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Diagnostics;

namespace Infrastructure.Services
{
    public class BalanceService : BaseService<BalanceData>, IBalanceService, INotificationHandler<PaymentReceivedEvent>
    {
        private readonly IAssetService _assetService;
        private readonly IMemoryCache _cache;
        private const string CACHE_KEY_USER_BALANCES = "user_balances_{0}_{1}"; // userId_assetClass
        private const int CACHE_DURATION_MINUTES = 5;

        public BalanceService(
            IAssetService assetService,
            IOptions<MongoDbSettings> mongoDbSettings,
            IMongoClient mongoClient,
            IMemoryCache cache,
            ILogger<BalanceService> logger)
            : base(
                  mongoClient,
                  mongoDbSettings,
                  "balances", logger,
                  cache
                  )
        {
            _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        public async Task<ResultWrapper<IEnumerable<BalanceData>>> GetAllByUserIdAsync(Guid userId, string? assetClass = null)
        {
            using var activity = new Activity("BalanceService.GetAllByUserIdAsync").Start();
            activity?.SetTag("userId", userId);
            activity?.SetTag("assetClass", assetClass ?? "all");

            try
            {
                // Try to get from cache first
                string cacheKey = string.Format(CACHE_KEY_USER_BALANCES, userId, assetClass ?? "all");
                if (_cache.TryGetValue(cacheKey, out IEnumerable<BalanceData> cachedBalances))
                {
                    _logger.LogInformation("Cache hit for user balances. UserId: {UserId}, AssetClass: {AssetClass}",
                        userId, assetClass ?? "all");
                    return ResultWrapper<IEnumerable<BalanceData>>.Success(cachedBalances);
                }

                _logger.LogInformation("Fetching balances for user {UserId} with assetClass {AssetClass}",
                    userId, assetClass ?? "all");

                // Fetch from database
                var filter = Builders<BalanceData>.Filter.Eq(b => b.UserId, userId);
                var balances = await GetAllAsync(filter);

                if (balances is null)
                {
                    throw new ArgumentNullException(nameof(balances),
                        $"Failed to retrieve balances for user {userId}");
                }

                // Filter by asset class if specified
                var returnBalances = new List<BalanceData>();
                if (assetClass != null && AssetClass.AllValues.Contains(assetClass))
                {
                    foreach (var balance in balances)
                    {
                        var assetData = await _assetService.GetByIdAsync(balance.AssetId);
                        if (assetData != null && assetData.Class.ToLowerInvariant() == assetClass.ToLowerInvariant())
                        {
                            returnBalances.Add(balance);
                        }
                    }
                }
                else
                {
                    returnBalances = balances.ToList();
                }

                // Cache the result
                var cacheEntryOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(TimeSpan.FromMinutes(CACHE_DURATION_MINUTES))
                    .SetSlidingExpiration(TimeSpan.FromMinutes(1));

                _cache.Set(cacheKey, returnBalances, cacheEntryOptions);

                _logger.LogInformation("Retrieved {Count} balances for user {UserId} with assetClass {AssetClass}",
                    returnBalances.Count, userId, assetClass ?? "all");

                return ResultWrapper<IEnumerable<BalanceData>>.Success(returnBalances);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting balances for user {UserId} with assetClass {AssetClass}: {Message}",
                    userId, assetClass ?? "all", ex.Message);
                return ResultWrapper<IEnumerable<BalanceData>>.FromException(ex);
            }
        }

        public async Task<List<BalanceDto>> FetchBalancesWithAssetsAsync(Guid userId)
        {
            using var activity = new Activity("BalanceService.FetchBalancesWithAssetsAsync").Start();
            activity?.SetTag("userId", userId);

            try
            {
                // Use efficient aggregation pipeline with lookup
                var pipeline = new List<BsonDocument>
                {
                    // Match balances for this user
                    new BsonDocument("$match", new BsonDocument("UserId", userId.ToString())),
                    
                    // Lookup asset data
                    new BsonDocument("$lookup", new BsonDocument
                    {
                        { "from", "assets" },
                        { "localField", "AssetId" },
                        { "foreignField", "_id" },
                        { "as", "AssetData" }
                    }),
                    
                    // Unwind the asset data array
                    new BsonDocument("$unwind", new BsonDocument("path", "$AssetData")),
                    
                    // Project into the DTO format
                    new BsonDocument("$project", new BsonDocument
                    {
                        { "_id", 0 },
                        { "AssetName", "$AssetData.Name" },
                        { "Ticker", "$AssetData.Ticker" },
                        { "Symbol", "$AssetData.Symbol" },
                        { "Available", "$Available" },
                        { "Locked", "$Locked" },
                        { "Total", "$Total" }
                    })
                };

                var result = await _collection.Aggregate<BalanceDto>(pipeline).ToListAsync();
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching balances with assets for user {UserId}: {Message}",
                    userId, ex.Message);
                throw;
            }
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
                string cacheKey = string.Format(CACHE_KEY_USER_BALANCES, userId, "all");
                _cache.Remove(cacheKey);

                // Also remove any asset-class specific caches
                foreach (var assetClassValue in AssetClass.AllValues)
                {
                    _cache.Remove(string.Format(CACHE_KEY_USER_BALANCES, userId, assetClassValue));
                }

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
                // Instead, this should be monitored and potentially retried through a separate mechanism
            }
        }
    }
}