using Application.Contracts.Requests.Subscription;
using Domain.DTOs;
using Domain.Interfaces;
using Domain.Models.Subscription;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Domain.Services
{
    public interface ISubscriptionService : IRepository<SubscriptionData>
    {
        public Task<ResultWrapper<ObjectId>> ProcessSubscriptionRequest(SubscriptionRequest request);
        public Task<ResultWrapper<IReadOnlyCollection<AllocationData>>> GetAllocationsAsync(ObjectId subscriptionId);
        public Task<UpdateResult> CancelAsync(ObjectId subscriptionId);
        public Task<IEnumerable<SubscriptionData>> GetUserSubscriptionsAsync(ObjectId userId);
    }
}