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
        // Inject the IOptions<MongoDbSettings>, singleton IMongoClient, and ILogger
        public SubscriptionService(
            IOptions<MongoDbSettings> mongoDbSettings,
            IMongoClient mongoClient,
            ILogger<SubscriptionService> logger)
            : base(mongoClient, mongoDbSettings, "subscriptions", logger)
        {
        }

        public async Task<FetchAllocationsResult> GetAllocationsAsync(ObjectId subscriptionId)
        {
            try
            {
                var subscription = await GetByIdAsync(subscriptionId);
                if (subscription == null)
                {
                    throw new KeyNotFoundException($"Subscription #{subscriptionId} not found.");
                }
                if (subscription.CoinAllocations == null || !subscription.CoinAllocations.Any())
                {
                    throw new ArgumentException("Subscription allocations cannot be empty/null.");
                }
                return FetchAllocationsResult.Success(subscription.CoinAllocations.ToList().AsReadOnly());
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
                return FetchAllocationsResult.Failure(reason, ex.Message);
            }
        }

    }
}
