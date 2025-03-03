using Application.Contracts.Requests.Subscription;
using Application.Interfaces;
using AspNetCore.Identity.MongoDbCore.Infrastructure;
using Domain.Constants;
using Domain.DTOs;
using Domain.Models.Subscription;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Infrastructure.Services
{
    public class SubscriptionService : BaseService<SubscriptionData>, ISubscriptionService
    {
        private readonly IBalanceService _balanceService;

        public SubscriptionService(
            IBalanceService balanceService,
            IOptions<MongoDbSettings> mongoDbSettings,
            IMongoClient mongoClient,
            ILogger<SubscriptionService> logger)
            : base(mongoClient, mongoDbSettings, "subscriptions", logger)
        {
            _balanceService = balanceService ?? throw new ArgumentNullException(nameof(balanceService));
        }

        protected override async Task OnInsertAsync(ObjectId insertedId)
        {
            var subscriptionData = await GetByIdAsync(insertedId);
            if (subscriptionData == null)
            {
                _logger.LogError("Inserted subscription {SubscriptionId} not found post-insert", insertedId);
                return;
            }

            var userId = subscriptionData.UserId;
            var assetsResult = await GetAllocationsAsync(insertedId);
            if (!assetsResult.IsSuccess)
            {
                _logger.LogWarning("Failed to get allocations for subscription {SubscriptionId}: {Error}", insertedId, assetsResult.ErrorMessage);
                return;
            }

            var assets = assetsResult.Data.Select(alloc => alloc.AssetId);
            await _balanceService.InitBalances(userId, insertedId, assets);
        }

        public async Task<ResultWrapper<ObjectId>> ProcessSubscriptionRequest(SubscriptionRequest request)
        {
            try
            {
                // Validate request
                if (request == null)
                {
                    throw new ArgumentNullException("Subscription request cannot be null.");
                }

                if (string.IsNullOrWhiteSpace(request.UserId) || !ObjectId.TryParse(request.UserId, out ObjectId userId))
                {
                    throw new ArgumentException($"Invalid UserId: {request.UserId}");
                }

                if (request.Allocations == null || !request.Allocations.Any())
                {
                    throw new ArgumentException("At least one allocation is required.");
                }

                foreach (var alloc in request.Allocations)
                {
                    if (string.IsNullOrWhiteSpace(alloc.AssetId) || !ObjectId.TryParse(alloc.AssetId, out _))
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

                var subscriptionData = new SubscriptionData
                {
                    UserId = userId,
                    Allocations = request.Allocations.Select(allocRequest => new AllocationData
                    {
                        AssetId = ObjectId.Parse(allocRequest.AssetId), // Safe due to prior validation
                        PercentAmount = allocRequest.PercentAmount
                    }),
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

                _logger.LogInformation("Successfully inserted subscription {SubscriptionId}", result.InsertedId);
                return ResultWrapper<ObjectId>.Success(result.InsertedId.AsObjectId);
            }
            catch (Exception ex)
            {
                string reason = ex switch
                {
                    ArgumentException => FailureReason.ValidationError,
                    MongoException => FailureReason.DatabaseError,
                    _ => FailureReason.Unknown
                };
                _logger.LogError(ex, "Failed to process subscription request: {Message}", ex.Message);
                return ResultWrapper<ObjectId>.Failure(reason, ex.Message);
            }
        }

        public async Task<ResultWrapper<IReadOnlyCollection<AllocationData>>> GetAllocationsAsync(ObjectId subscriptionId)
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
                string reason = ex switch
                {
                    ArgumentException => FailureReason.ValidationError,
                    KeyNotFoundException => FailureReason.DataNotFound,
                    _ => FailureReason.Unknown
                };
                _logger.LogError(ex, "Fetch subscription failed: {Message}", ex.Message);
                return ResultWrapper<IReadOnlyCollection<AllocationData>>.Failure(reason, ex.Message);
            }
        }

        public async Task<UpdateResult> CancelAsync(ObjectId subscriptionId)
        {
            var updatedFields = new { IsCancelled = true };
            return await UpdateOneAsync(subscriptionId, updatedFields);
        }

        public async Task<IEnumerable<SubscriptionData>> GetUserSubscriptionsAsync(ObjectId userId)
        {
            var filter = Builders<SubscriptionData>.Filter.Eq(doc => doc.UserId, userId);
            return await GetAllAsync(filter);
        }
    }
}