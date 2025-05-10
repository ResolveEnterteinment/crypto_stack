// Application/Interfaces/Subscription/ISubscriptionRetryService.cs
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