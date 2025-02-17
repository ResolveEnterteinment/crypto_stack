using AspNetCore.Identity.MongoDbCore.Infrastructure;
using Domain.Models.Subscription;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Infrastructure.Services
{
    public class SubscriptionService : ISubscriptionService
    {
        private readonly IMongoCollection<SubscriptionData> _subscriptions;
        private readonly ILogger<SubscriptionService> _logger;

        // Inject the IOptions<MongoDbSettings>, singleton IMongoClient, and ILogger
        public SubscriptionService(
            IOptions<MongoDbSettings> mongoDbSettings,
            IMongoClient mongoClient,
            ILogger<SubscriptionService> logger)
        {
            _logger = logger;
            var databaseName = mongoDbSettings.Value.DatabaseName;
            var mongoDatabase = mongoClient.GetDatabase(databaseName);
            _subscriptions = mongoDatabase.GetCollection<SubscriptionData>("subscriptions");
        }

        public async Task<IEnumerable<CoinAllocation>> GetCoinAllocationsAsync(ObjectId subscriptionId)
        {
            try
            {
                var filter = Builders<SubscriptionData>.Filter.Eq(s => s._id, subscriptionId);
                var subscription = await _subscriptions.Find(filter).FirstOrDefaultAsync();
                if (subscription == null)
                {
                    throw new KeyNotFoundException("Subscription not found.");
                }
                return subscription.CoinAllocations;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fetch subscription failed: {Message}", ex.Message);
                return new List<CoinAllocation>();
            }
        }
    }
}
