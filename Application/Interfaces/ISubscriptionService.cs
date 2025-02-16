using Domain.Models.Subscription;
using MongoDB.Bson;

namespace Infrastructure.Services
{
    public interface ISubscriptionService
    {
        public Task<IEnumerable<CoinAllocation>> GetCoinAllocationsAsync(ObjectId subscriptionId);
    }
}