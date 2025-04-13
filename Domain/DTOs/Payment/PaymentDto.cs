namespace Domain.DTOs.Payment
{
    public class PaymentDto
    {
        public required string Id { get; set; }
        public required DateTime CreatedAt { get; set; }
        public long AmountDue { get; set; }
        public long AmountPaid { get; set; }
        public long AmountRemaining { get; set; }
        public string Currency { get; set; }
        public string CustomerEmail { get; set; }
        public DateTime? DueDate { get; set; }
        public bool Paid { get; set; }
        public long? Tax { get; set; }
        public long? Discount { get; set; }
        public long Total { get; set; }
        public string PaymentIntentId { get; set; }
        public string Status { get; set; }
    }
}
