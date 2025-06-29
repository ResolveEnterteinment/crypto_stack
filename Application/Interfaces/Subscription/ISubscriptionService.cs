using Application.Contracts.Requests.Subscription;
using Application.Interfaces.Base;
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
        INotificationHandler<CheckoutSessionCompletedEvent>,
        INotificationHandler<PaymentMethodUpdatedEvent>,
        INotificationHandler<SubscriptionReactivationRequestedEvent>
    {
        Task<ResultWrapper<CrudResult>> CreateAsync(SubscriptionCreateRequest request);
        Task<ResultWrapper<CrudResult>> UpdateAsync(Guid id, SubscriptionUpdateRequest request);
        Task<ResultWrapper<List<AllocationDto>>> GetAllocationsAsync(Guid subscriptionId);
        Task<ResultWrapper<List<SubscriptionDto>>> GetAllByUserIdAsync(Guid userId, string? statusFilter = null);
        Task<ResultWrapper<CrudResult>> UpdateSubscriptionStatusAsync(Guid subscriptionId, string status);
        Task<ResultWrapper> ReactivateSubscriptionAsync(Guid subscriptionId);
        Task<ResultWrapper> CancelAsync(Guid subscriptionId);
        Task<ResultWrapper> DeleteAsync(Guid subscriptionId);
        Task Handle(CheckoutSessionCompletedEvent notification, CancellationToken cancellationToken);
        Task Handle(SubscriptionCreatedEvent notification, CancellationToken cancellationToken);
        Task Handle(PaymentReceivedEvent notification, CancellationToken cancellationToken);
        Task Handle(PaymentCancelledEvent notification, CancellationToken cancellationToken);
        Task TestLog();
    }
}