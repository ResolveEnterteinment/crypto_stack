using AspNetCore.Identity.MongoDbCore.Infrastructure;
using Domain.Constants;
using Domain.DTOs;
using Domain.Models.Subscription;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using static MongoDB.Driver.UpdateResult;

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

        public async Task<FetchAllocationsResult> GetAllocationsAsync(ObjectId subscriptionId)
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

        public async Task<UpdateResult> UpdateBalances(ObjectId subscriptionId, IEnumerable<BalanceData> updateBalances)
        {
            try
            {
                var subscription = await GetByIdAsync(subscriptionId);
                if (subscription is null)
                    throw new KeyNotFoundException($"Failed to fetch subscription data for id #{subscriptionId}. {nameof(subscription)} returned null.");

                var balances = subscription.Balances?.ToList() ?? new List<BalanceData>();
                foreach (var newBalance in updateBalances)
                {
                    var balance = balances.Find(b => b.CoinId == newBalance.CoinId);
                    if (balance is null)
                    {
                        balances.Add(newBalance);
                    }
                    else
                    {
                        balance.Quantity += newBalance.Quantity;
                    }
                }

                // Directly build the update definition
                var updateDefinition = Builders<SubscriptionData>.Update.Set(s => s.Balances, balances);
                var filter = Builders<SubscriptionData>.Filter.Eq(s => s._id, subscriptionId);
                var result = await _collection.UpdateOneAsync(filter, updateDefinition).ConfigureAwait(false);
                _logger.LogInformation($"Updated subscription {subscriptionId} balances. Updated balances: {balances.Count} items");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to update subscription balances: {Message}", ex.Message);
                return Unacknowledged.Instance;
            }
        }
    }
}