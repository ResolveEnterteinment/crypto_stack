using Application.Contracts.Requests.Subscription;
using Domain.DTOs;
using Domain.Events;
using Domain.Interfaces;
using Domain.Models.Subscription;
using MongoDB.Driver;

namespace Application.Interfaces
{
    public interface ISubscriptionService : IRepository<SubscriptionData>
    {
        public Task<ResultWrapper<Guid>> Create(SubscriptionCreateRequest request);
        public Task<ResultWrapper<long>> Update(Guid id, SubscriptionUpdateRequest request);
        public Task<ResultWrapper<IReadOnlyCollection<AllocationData>>> GetAllocationsAsync(Guid subscriptionId);
        public Task<ResultWrapper<IEnumerable<SubscriptionData>>> GetAllByUserIdAsync(Guid userId);
        public Task<UpdateResult> CancelAsync(Guid subscriptionId);
        public Task Handle(SubscriptionCreatedEvent notification, CancellationToken cancellationToken);
    }
}