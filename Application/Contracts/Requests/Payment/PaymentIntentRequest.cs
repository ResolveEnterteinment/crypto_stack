namespace Application.Contracts.Requests.Payment
{
    public class PaymentIntentRequest
    {
        public required Guid UserId { get; set; }
        public required string SubscriptionId { get; set; }
        public required string PaymentId { get; set; }
        public required string Currency { get; set; }
        public required decimal TotalAmount { get; set; }
        public required decimal PaymentProviderFee { get; set; }
        public required decimal PlatformFee { get; set; }
        public required decimal NetAmount { get; set; }
        public required string Status { get; set; }
    }
}
