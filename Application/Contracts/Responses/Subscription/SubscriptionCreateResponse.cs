namespace Application.Contracts.Responses.Subscription
{
    /// <summary>
    /// Enhanced subscription create response with additional flow information
    /// </summary>
    public class SubscriptionCreateResponse
    {
        public string Id { get; set; }
        public string CheckoutUrl { get; set; }
        public string Status { get; set; }
        public bool RequiresPaymentSetup => !string.IsNullOrEmpty(CheckoutUrl);
    }
}
