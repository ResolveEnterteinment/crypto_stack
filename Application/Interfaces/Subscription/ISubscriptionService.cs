using Application.Contracts.Requests.Subscription;
using Application.Interfaces.Base;
using Domain.Constants;
using Domain.DTOs;
using Domain.DTOs.Subscription;
using Domain.Events;
using Domain.Models.Subscription;
using MediatR;

namespace Application.Interfaces.Subscription
{
    public interface ISubscriptionService :
        IBaseService<SubscriptionData>,
        INotificationHandler<PaymentReceivedEvent>,
        INotificationHandler<PaymentCancelledEvent>,
        INotificationHandler<SubscriptionCreatedEvent>,
        INotificationHandler<CheckoutSessionCompletedEvent>
    {
        public Task<ResultWrapper<CrudResult>> CreateAsync(SubscriptionCreateRequest request);
        public Task<ResultWrapper> UpdateAsync(Guid id, SubscriptionUpdateRequest request);
        public Task<ResultWrapper<List<AllocationDto>>> GetAllocationsAsync(Guid subscriptionId);
        public Task<ResultWrapper<List<SubscriptionDto>>> GetAllByUserIdAsync(Guid userId);
        public Task<ResultWrapper<CrudResult>> UpdateSubscriptionStatusAsync(Guid subscriptionId, SubscriptionStatus status);
        public Task<ResultWrapper> CancelAsync(Guid subscriptionId);
        public Task Handle(CheckoutSessionCompletedEvent notification, CancellationToken cancellationToken);
        public Task Handle(SubscriptionCreatedEvent notification, CancellationToken cancellationToken);
        public Task Handle(PaymentReceivedEvent notification, CancellationToken cancellationToken);
        public Task Handle(PaymentCancelledEvent notification, CancellationToken cancellationToken);
        Task TestLog();
    }
}