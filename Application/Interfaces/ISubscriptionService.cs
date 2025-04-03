using Application.Contracts.Requests.Subscription;
using Domain.Constants;
using Domain.DTOs;
using Domain.DTOs.Subscription;
using Domain.Events;
using Domain.Models.Subscription;
using MongoDB.Driver;

namespace Application.Interfaces
{
    public interface ISubscriptionService : IRepository<SubscriptionData>
    {
        public Task<ResultWrapper<Guid>> CreateAsync(SubscriptionCreateRequest request);
        public Task<ResultWrapper<long>> UpdateAsync(Guid id, SubscriptionUpdateRequest request);
        public Task<ResultWrapper<IEnumerable<AllocationDto>>> GetAllocationsAsync(Guid subscriptionId);
        public Task<ResultWrapper<IEnumerable<SubscriptionDto>>> GetAllByUserIdAsync(Guid userId);
        public Task<UpdateResult> CancelAsync(Guid subscriptionId);
        public Task Handle(SubscriptionCreatedEvent notification, CancellationToken cancellationToken);

        /// <summary>
        /// Updates the status of a subscription
        /// </summary>
        /// <param name="subscriptionId">The ID of the subscription</param>
        /// <param name="status">The new status</param>
        /// <returns>The result of the update operation</returns>
        Task<UpdateResult> UpdateSubscriptionStatusAsync(Guid subscriptionId, SubscriptionStatus status);
    }
}