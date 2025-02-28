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
        public SubscriptionService(
            IOptions<MongoDbSettings> mongoDbSettings,
            IMongoClient mongoClient,
            ILogger<SubscriptionService> logger)
            : base(mongoClient, mongoDbSettings, "subscriptions", logger)
        {
        }

        public async Task<ResultWrapper<IReadOnlyCollection<CoinAllocationData>>> GetAllocationsAsync(ObjectId subscriptionId)
        {
            // Unchanged
            try
            {
                var subscription = await GetByIdAsync(subscriptionId);
                if (subscription == null)
                {
                    throw new KeyNotFoundException($"Subscription #{subscriptionId} not found.");
                }
                if (subscription.CoinAllocations == null || !subscription.CoinAllocations.Any())
                {
                    throw new ArgumentException($"Coin allocation fetch error. No allocation(s) found for subscription #{subscriptionId}.");
                }
                return ResultWrapper<IReadOnlyCollection<CoinAllocationData>>.Success(subscription.CoinAllocations.ToList().AsReadOnly());
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
                return ResultWrapper<IReadOnlyCollection<CoinAllocationData>>.Failure(reason, ex.Message);
            }
        }

        public async Task<UpdateResult> CancelAsync(ObjectId subscriptionId)
        {
            var updatedFields = new { IsCancelled = true };
            return await UpdateAsync(subscriptionId, updatedFields);
        }

        public async Task<IEnumerable<SubscriptionData>> GetUserSubscriptionsAsync(ObjectId userId)
        {
            var filter = Builders<SubscriptionData>.Filter.Eq(doc => doc.UserId, userId);
            return await GetAllAsync(filter);
        }

    }
}