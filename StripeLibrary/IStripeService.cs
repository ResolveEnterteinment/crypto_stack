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
        Task<ResultWrapper> RetryPaymentAsync(string paymentIntentId, string subscriptionId);
        Task<ResultWrapper<SessionDto>> CreateUpdatePaymentMethodSessionAsync(
            string subscriptionId,
            Dictionary<string, string> metadata);
        Task<ResultWrapper> UpdateSubscriptionAsync(
            string stripeSubscriptionId,
            string localSubscriptionId,
            decimal? newAmount = null,
            DateTime? newEndDate = null);
        Task<ResultWrapper> CancelSubscription(string subscriptionId);
    }
}