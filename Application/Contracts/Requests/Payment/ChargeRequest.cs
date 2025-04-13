namespace Application.Contracts.Requests.Payment
{
    public class ChargeRequest
    {
        public required string Id { get; set; }
        public required string PaymentIntentId { get; set; }
        public required string InvoiceId { get; set; }
        public required long Amount { get; set; }
        public required string Currency { get; set; }

    }
}
