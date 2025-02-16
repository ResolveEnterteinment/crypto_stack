using AspNetCore.Identity.MongoDbCore.Infrastructure;
using DnsClient.Internal;
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
        public SubscriptionService(IOptions<MongoDbSettings> mongoDbSettings, ILogger<SubscriptionService> logger)
        {
            var mongoClient = new MongoClient(mongoDbSettings.Value.ConnectionString);

            var mongoDatabase = mongoClient.GetDatabase(
                mongoDbSettings.Value.DatabaseName);

            _subscriptions = mongoDatabase.GetCollection<SubscriptionData>("subscriptions");
            _logger = logger;
        }
        public async Task<IEnumerable<CoinAllocation>> GetCoinAllocationsAsync(ObjectId subscriptionId)
        {
            try
            {
                var filter = Builders<SubscriptionData>.Filter
                .Eq(s => s._id, subscriptionId);
                // Asynchronously retrieves the first document that matches the filter
                var subscription = await _subscriptions.Find(filter).FirstOrDefaultAsync();
                return subscription == null ? throw new KeyNotFoundException("Subscription not found.") : subscription.CoinAllocations;
            }
            catch (Exception ex)
            {
                _logger.LogError("Fetch subscription failed: ${message}", ex.Message);
                return new List<CoinAllocation>();
            }
        }
    }
}
