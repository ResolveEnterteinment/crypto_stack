using Stripe;

namespace StripeLibrary
{
    public interface IStripeService
    {
        public Task<PaymentIntent> GetPaymentIntentAsync(string id, PaymentIntentGetOptions? options = null);
    }
}
