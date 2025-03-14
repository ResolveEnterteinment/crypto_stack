using Application.Contracts.Requests.Subscription;
using Domain.DTOs;
using Domain.Interfaces;
using Domain.Models.Subscription;
using MongoDB.Driver;

namespace Application.Interfaces
{
    public interface ISubscriptionService : IRepository<SubscriptionData>
    {
        public Task<ResultWrapper<Guid>> ProcessSubscriptionCreateRequest(SubscriptionCreateRequest request);
        public Task<ResultWrapper<IReadOnlyCollection<AllocationData>>> GetAllocationsAsync(Guid subscriptionId);
        public Task<ResultWrapper<long>> ProcessSubscriptionUpdateRequest(Guid id, SubscriptionUpdateRequest request);
        public Task<IEnumerable<SubscriptionData>> GetAllByUserIdAsync(Guid userId);
        public Task<UpdateResult> CancelAsync(Guid subscriptionId);
    }
}