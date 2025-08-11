// Application/Interfaces/Subscription/ISubscriptionRetryService.cs
using Application.Interfaces.Base;
using Domain.Models.Subscription;

namespace Application.Interfaces.Subscription
{
    public interface ISubscriptionRetryService
    {
        /// <summary>
        /// Process any pending payment retries that are due
        /// </summary>
        Task ProcessPendingRetriesAsync();
    }
}