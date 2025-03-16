using Application.Contracts.Requests.Subscription;
using Application.Interfaces;
using Domain.Constants;
using Domain.DTOs;
using Domain.Models.Subscription;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Infrastructure.Services
{
    public class SubscriptionService : BaseService<SubscriptionData>, ISubscriptionService
    {
        private readonly IBalanceService _balanceService;
        private readonly IAssetService _assetService;
        private readonly INotificationService _notificationService;

        public SubscriptionService(
            IBalanceService balanceService,
            IAssetService assetService,
            IOptions<MongoDbSettings> mongoDbSettings,
            IMongoClient mongoClient,
            ILogger<SubscriptionService> logger,
            INotificationService notificationService
            )
            : base(mongoClient, mongoDbSettings, "subscriptions", logger)
        {
            _balanceService = balanceService ?? throw new ArgumentNullException(nameof(balanceService));
            _assetService = assetService ?? throw new ArgumentNullException(nameof(_assetService));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(_notificationService));
        }

        public async Task<ResultWrapper<Guid>> ProcessSubscriptionCreateRequest(SubscriptionCreateRequest request)
        {
            try
            {
                // Validate request
                #region Validate
                if (request == null)
                {
                    throw new ArgumentNullException("Subscription request cannot be null.");
                }

                if (string.IsNullOrWhiteSpace(request.UserId) || !Guid.TryParse(request.UserId, out Guid userId))
                {
                    throw new ArgumentException($"Invalid UserId: {request.UserId}");
                }

                if (request.Allocations == null || !request.Allocations.Any())
                {
                    throw new ArgumentException("At least one allocation is required.");
                }

                foreach (var alloc in request.Allocations)
                {
                    if (string.IsNullOrWhiteSpace(alloc.AssetId) || !Guid.TryParse(alloc.AssetId, out _))
                    {
                        throw new ArgumentException($"Invalid AssetId: {alloc.AssetId}");
                    }
                    if (alloc.PercentAmount > 100)
                    {
                        throw new ArgumentOutOfRangeException($"PercentAmount must be between 0 and 100: {alloc.PercentAmount}");
                    }
                }

                if (string.IsNullOrWhiteSpace(request.Interval))
                {
                    throw new ArgumentNullException("Interval is required.");
                }

                if (request.Amount <= 0)
                {
                    throw new ArgumentOutOfRangeException("Amount must be greater than zero.");
                }
                #endregion Validate

                List<AllocationData> allocations = request.Allocations.Select<AllocationRequest, AllocationData>(a => new AllocationData()
                {
                    AssetId = Guid.Parse(a.AssetId),
                    AssetTicker = "",
                    PercentAmount = a.PercentAmount,
                }).ToList();

                allocations.ForEach(async a =>
                {
                    var asset = await _assetService.GetByIdAsync(a.AssetId);
                    a.AssetTicker = asset.Ticker;
                });

                var subscriptionData = new SubscriptionData
                {
                    UserId = userId,
                    Allocations = allocations,
                    Interval = request.Interval,
                    Amount = request.Amount,
                    EndDate = request.EndDate,
                    IsCancelled = request.IsCancelled
                };

                var result = await InsertOneAsync(subscriptionData);
                if (!result.IsAcknowledged)
                {
                    throw new MongoException("Failed to insert subscription into database.");
                }
                var insertedId = result.InsertedId.Value;
                var assetsResult = await GetAllocationsAsync(insertedId);
                if (!assetsResult.IsSuccess)
                {
                    _logger.LogWarning("Failed to get allocations for subscription {SubscriptionId}: {Error}", insertedId, assetsResult.ErrorMessage);
                    throw new KeyNotFoundException($"Failed to get allocations for subscription {insertedId}: {assetsResult.ErrorMessage}");
                }
                _logger.LogInformation("Successfully inserted subscription {SubscriptionId}", result.InsertedId);
                await _notificationService.CreateNotificationAsync(new()
                {
                    UserId = userId.ToString(),
                    Message = "A new subscription is created."
                });
                return ResultWrapper<Guid>.Success(result.InsertedId.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to process subscription request: {ex.Message}");
                return ResultWrapper<Guid>.Failure(FailureReason.From(ex), ex.Message);
            }
        }
        public async Task<ResultWrapper<long>> ProcessSubscriptionUpdateRequest(Guid id, SubscriptionUpdateRequest request)
        {
            try
            {
                // Validate request
                #region Validate
                if (id == Guid.Empty)
                {
                    throw new ArgumentNullException("Subscription id cannot be null.");
                }
                if (request == null)
                {
                    throw new ArgumentNullException("Subscription update request cannot be null.");
                }

                if (request.Allocations != null && !request.Allocations.Any())
                {
                    throw new ArgumentException("At least one allocation is required.");
                }
                if (request.Allocations != null && request.Allocations.Any())
                {
                    foreach (var alloc in request.Allocations)
                    {
                        if (string.IsNullOrWhiteSpace(alloc.AssetId) || !Guid.TryParse(alloc.AssetId, out _))
                        {
                            throw new ArgumentException($"Invalid AssetId: {alloc.AssetId}");
                        }
                        if (alloc.PercentAmount > 100)
                        {
                            throw new ArgumentOutOfRangeException($"PercentAmount must be between 0 and 100. Found: {alloc.PercentAmount}");
                        }
                    }
                }
                if (!String.IsNullOrEmpty(request.Interval))
                {
                    Type type = typeof(SubscriptionInterval);

                    if (!SubscriptionInterval.AllValues.Contains(request.Interval))
                    {
                        throw new ArgumentException($"Invalid interval. Must one of {String.Join(", ", SubscriptionInterval.AllValues)}. Found:  {request.Interval}");
                    }

                }

                if (request.Amount != null && request.Amount <= 0)
                {
                    throw new ArgumentOutOfRangeException("Amount must be greater than zero.");
                }
                #endregion Validate

                var updateFields = new Dictionary<string, object>();
                List<AllocationData> allocations = new List<AllocationData>();
                if (request.Allocations != null && request.Allocations.Any())
                {
                    foreach (var allocation in request.Allocations)
                    {
                        var assetData = await _assetService.GetByIdAsync(Guid.Parse(allocation.AssetId));
                        allocations.Add(new()
                        {
                            AssetId = Guid.Parse(allocation.AssetId),
                            AssetTicker = assetData.Ticker,
                            PercentAmount = allocation.PercentAmount,
                        });
                    }
                }

                if (allocations is not null && allocations.Any()) updateFields["Allocations"] = allocations;
                if (request.Interval is not null) updateFields["Interval"] = request.Interval;
                if (request.Amount is not null) updateFields["Amount"] = request.Amount;
                if (request.EndDate is not null) updateFields["EndDate"] = request.EndDate;

                var updateResult = await UpdateOneAsync(id, updateFields);
                if (!updateResult.IsAcknowledged)
                {
                    throw new MongoException("Failed to update subscription data.");
                }

                _logger.LogInformation($"Successfully updated subscription record: {updateResult.ModifiedCount}");
                return ResultWrapper<long>.Success(updateResult.ModifiedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to process subscription request: {ex.Message}");
                return ResultWrapper<long>.Failure(FailureReason.From(ex), ex.Message);
            }
        }

        public async Task<ResultWrapper<IReadOnlyCollection<AllocationData>>> GetAllocationsAsync(Guid subscriptionId)
        {
            try
            {
                var subscription = await GetByIdAsync(subscriptionId);
                if (subscription == null)
                {
                    throw new KeyNotFoundException($"Subscription #{subscriptionId} not found.");
                }
                if (subscription.Allocations == null || !subscription.Allocations.Any())
                {
                    throw new ArgumentException($"Coin allocation fetch error. No allocation(s) found for subscription #{subscriptionId}.");
                }
                return ResultWrapper<IReadOnlyCollection<AllocationData>>.Success(subscription.Allocations.ToList().AsReadOnly());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fetch subscription failed: {Message}", ex.Message);
                return ResultWrapper<IReadOnlyCollection<AllocationData>>.Failure(FailureReason.From(ex), ex.Message);
            }
        }
        public async Task<ResultWrapper<IEnumerable<SubscriptionData>>> GetAllByUserIdAsync(Guid userId)
        {
            try
            {
                var filter = Builders<SubscriptionData>.Filter.Eq(doc => doc.UserId, userId);
                var subscriptions = await GetAllAsync(filter);
                return ResultWrapper<IEnumerable<SubscriptionData>>.Success(subscriptions);
            }
            catch (Exception ex)
            {
                return ResultWrapper<IEnumerable<SubscriptionData>>.Failure(FailureReason.From(ex), ex.Message);
            }

        }
        public async Task<UpdateResult> CancelAsync(Guid subscriptionId)
        {
            var updatedFields = new { IsCancelled = true };
            return await UpdateOneAsync(subscriptionId, updatedFields);
        }
    }
}