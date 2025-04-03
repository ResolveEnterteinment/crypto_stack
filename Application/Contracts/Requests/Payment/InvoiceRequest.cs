namespace Application.Contracts.Requests.Payment
{
    public class InvoiceRequest
    {
        public string Id { get; set; }
        public string UserId { get; set; }
        public string SubscriptionId { get; set; }
        public long AmountPaid { get; set; }
        public string Currency { get; set; }

    }
}
