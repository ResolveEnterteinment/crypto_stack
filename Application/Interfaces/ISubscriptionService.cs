using Application.Contracts.Requests.Subscription;
using Domain.DTOs;
using Domain.DTOs.Subscription;
using Domain.Events;
using Domain.Models.Subscription;
using MongoDB.Driver;

namespace Application.Interfaces
{
    public interface ISubscriptionService : IRepository<SubscriptionData>
    {
        public Task<ResultWrapper<Guid>> Create(SubscriptionCreateRequest request);
        public Task<ResultWrapper<long>> Update(Guid id, SubscriptionUpdateRequest request);
        public Task<ResultWrapper<IEnumerable<AllocationDto>>> GetAllocationsAsync(Guid subscriptionId);
        public Task<ResultWrapper<IEnumerable<SubscriptionDto>>> GetAllByUserIdAsync(Guid userId);
        public Task<UpdateResult> CancelAsync(Guid subscriptionId);
        public Task Handle(SubscriptionCreatedEvent notification, CancellationToken cancellationToken);
    }
}