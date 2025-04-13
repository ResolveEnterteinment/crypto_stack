namespace Domain.DTOs.Payment
{
    public class InvoiceDto
    {
        public required string Provider { get; set; }
        public required string Id { get; set; }
        public required string PaymentIntentId { get; set; }
    }
}
