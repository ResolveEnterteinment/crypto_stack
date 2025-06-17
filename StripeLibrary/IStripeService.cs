using Domain.DTOs;
using Domain.DTOs.Payment;
using Stripe;

namespace StripeLibrary
{
    public interface IStripeService
    {
        public Task<PaymentIntent> GetPaymentIntentAsync(string id, PaymentIntentGetOptions? options = null);
        public Task<Invoice> GetInvoiceAsync(string id);
        public Task<IEnumerable<Customer>> SearchCustomersAsync(Dictionary<string, object> searchOptions);
        public Task<Customer> CreateCustomerAsync(Dictionary<string, object> customerOptions);
        public Task<IEnumerable<Invoice>> GetSubscriptionInvoicesAsync(string subscriptionId);
        public Task<IEnumerable<Subscription>> SearchSubscriptionsByMetadataAsync(string metadataKey, string metadataValue);
        Task<ResultWrapper> RetryPaymentAsync(string paymentIntentId, string subscriptionId);
        Task<ResultWrapper<SessionDto>> CreateUpdatePaymentMethodSessionAsync(
            string subscriptionId,
            Dictionary<string, string> metadata);
    }
}