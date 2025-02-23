using Domain.DTOs;
using Domain.Interfaces;
using Domain.Models.Subscription;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Infrastructure.Services
{
    public interface ISubscriptionService : IRepository<SubscriptionData>
    {
        public Task<FetchAllocationsResult> GetAllocationsAsync(ObjectId subscriptionId);
        public Task<UpdateResult> CancelAsync(ObjectId subscriptionId);
        public Task<IEnumerable<SubscriptionData>> GetUserSubscriptionsAsync(ObjectId userId);
        public Task<UpdateResult> UpdateBalances(ObjectId subscriptionId, IEnumerable<BalanceData> balances);
    }
}