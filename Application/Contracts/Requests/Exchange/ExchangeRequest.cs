namespace Application.Contracts.Requests.Exchange
{
    public class ExchangeRequest
    {

        public required string Id { get; set; }
        public required DateTime CreateTime { get; set; }
        public required string UserId { get; set; }
        public required string SubscriptionId { get; set; }
        public required string PaymentProviderId { get; set; }
        public required decimal TotalAmount { get; set; }
        public required decimal PaymentProviderFee { get; set; }
        public required decimal PlatformFee { get; set; }
        public required decimal NetAmount { get; set; }
        public required string Status { get; set; }
    }
}
