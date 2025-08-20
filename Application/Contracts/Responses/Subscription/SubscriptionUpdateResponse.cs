namespace Application.Contracts.Responses.Subscription
{
    public class SubscriptionUpdateResponse
    {
        public long ModifiedCount { get; set; }
        public bool LocalUpdated { get; set; }
        public bool PaymentProviderUpdated { get; set; }
    }
}
