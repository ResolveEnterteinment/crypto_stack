namespace Application.Contracts.Requests.Payment
{
    public class InvoiceRequest
    {
        public string Id { get; set; }
        public string Provider { get; set; }
        public string ChargeId { get; set; }
        public string PaymentIntentId { get; set; }
        public string UserId { get; set; }
        public string SubscriptionId { get; set; }
        public string ProviderSubscripitonId { get; set; }
        public Dictionary<string, string> Metadata { get; set; }
        public long Amount { get; set; }
        public string Currency { get; set; }
        public string Status { get; set; }
    }
}
