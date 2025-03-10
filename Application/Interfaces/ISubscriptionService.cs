using Application.Contracts.Requests.Subscription;
using Domain.DTOs;
using Domain.Interfaces;
using Domain.Models.Subscription;
using MongoDB.Driver;

namespace Application.Interfaces
{
    public interface ISubscriptionService : IRepository<SubscriptionData>
    {
        public Task<ResultWrapper<Guid>> ProcessSubscriptionRequest(SubscriptionRequest request);
        public Task<ResultWrapper<IReadOnlyCollection<AllocationData>>> GetAllocationsAsync(Guid subscriptionId);
        public Task<UpdateResult> CancelAsync(Guid subscriptionId);
        public Task<IEnumerable<SubscriptionData>> GetUserSubscriptionsAsync(Guid userId);
    }
}