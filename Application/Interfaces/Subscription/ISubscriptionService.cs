using Application.Contracts.Requests.Subscription;
using Application.Interfaces.Base;
using Domain.DTOs;
using Domain.DTOs.Subscription;
using Domain.Events.Payment;
using Domain.Events.Subscription;
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
        Task<ResultWrapper<CrudResult<SubscriptionData>>> CreateAsync(SubscriptionCreateRequest request);
        Task<ResultWrapper<CrudResult<SubscriptionData>>> UpdateAsync(Guid id, SubscriptionUpdateRequest request);
        Task<ResultWrapper<List<AllocationDto>>> GetAllocationsAsync(Guid subscriptionId);
        Task<ResultWrapper<List<EnhancedAllocationDto>>> GetEnhancedAllocationsAsync(Guid subscriptionId, bool includePerformanceData = false);
        Task<ResultWrapper<List<SubscriptionDto>>> GetAllByUserIdAsync(Guid userId, string? statusFilter = null);
        Task<ResultWrapper<CrudResult<SubscriptionData>>> UpdateSubscriptionStatusAsync(Guid subscriptionId, string status);
        Task<ResultWrapper> ReactivateSubscriptionAsync(Guid subscriptionId);
        Task<ResultWrapper<CrudResult<SubscriptionData>>> CancelAsync(Guid subscriptionId);
        Task<ResultWrapper> PauseAsync(Guid subscriptionId);
        Task<ResultWrapper> ResumeAsync(Guid subscriptionId);
        Task<ResultWrapper> OnPauseAsync(Guid subscriptionId);
        Task<ResultWrapper> OnResumeAsync(Guid subscriptionId);
        Task<ResultWrapper<CrudResult<SubscriptionData>>> DeleteAsync(Guid subscriptionId);
    }
}