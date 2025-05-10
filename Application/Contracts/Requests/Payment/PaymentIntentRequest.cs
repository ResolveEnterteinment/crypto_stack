namespace Application.Contracts.Requests.Payment
{
    public class PaymentIntentRequest
    {
        public required string UserId { get; set; }
        public required string SubscriptionId { get; set; }
        public required string Provider { get; set; }
        public required string PaymentId { get; set; }
        public required string InvoiceId { get; set; }
        public required string Currency { get; set; }
        public required decimal Amount { get; set; }
        public required string Status { get; set; }
        public string? LastPaymentError { get; set; }
    }
}
