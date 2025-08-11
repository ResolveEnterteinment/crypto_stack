using Domain.DTOs;
using Domain.DTOs.Payment;
using Stripe;

namespace StripeLibrary
{
    public interface IStripeService
    {
        Task<PaymentIntent> GetPaymentIntentAsync(string id, PaymentIntentGetOptions? options = null);
        Task<Invoice> GetInvoiceAsync(string id);
        Task<IEnumerable<Customer>> SearchCustomersAsync(Dictionary<string, object> searchOptions);
        Task<Customer> CreateCustomerAsync(Dictionary<string, object> customerOptions);
        Task<bool> CheckCustomerExists(string customerId);
        Task<IEnumerable<Invoice>> GetSubscriptionInvoicesAsync(string subscriptionId);
        Task<IEnumerable<Subscription>> SearchSubscriptionsByMetadataAsync(string metadataKey, string metadataValue);
        Task<ResultWrapper<PaymentIntent>> RetryPaymentAsync(string paymentIntentId, string subscriptionId);
        Task<ResultWrapper<SessionDto>> CreateUpdatePaymentMethodSessionAsync(
            string subscriptionId,
            Dictionary<string, string> metadata);
        Task<ResultWrapper> UpdateSubscriptionAsync(
            string stripeSubscriptionId,
            string localSubscriptionId,
            decimal? newAmount = null,
            DateTime? newEndDate = null);
        Task<ResultWrapper> CancelSubscription(string subscriptionId);

        /// <summary>
        /// Pauses a Stripe subscription by setting pause_collection
        /// </summary>
        /// <param name="subscriptionId">The Stripe subscription ID to pause</param>
        /// <returns>Result of the pause operation</returns>
        Task<ResultWrapper> PauseSubscriptionAsync(string subscriptionId);

        /// <summary>
        /// Resumes a paused Stripe subscription by removing pause_collection
        /// </summary>
        /// <param name="subscriptionId">The Stripe subscription ID to resume</param>
        /// <returns>Result of the resume operation</returns>
        Task<ResultWrapper> ResumeSubscriptionAsync(string subscriptionId);
    }
}