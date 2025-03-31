namespace Application.Contracts.Responses
{
    public class SubscriptionCreateResponse
    {
        public string SubscriptionId { get; set; }
        public string PaymentUrl { get; set; } // Stripe checkout URL
    }
}
