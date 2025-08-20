namespace Domain.Models.Payment
{
    public class PaymentData : BaseEntity
    {
        public required Guid UserId { get; set; }
        public Guid SubscriptionId { get; set; }
        public required string Provider { get; set; }
        public required string InvoiceId { get; set; }
        public required string PaymentProviderId { get; set; }
        public string ProviderSubscriptionId { get; set; }
        public required decimal TotalAmount { get; set; }
        public required decimal PaymentProviderFee { get; set; }
        public required decimal PlatformFee { get; set; }
        public required decimal NetAmount { get; set; }
        public required string Currency { get; set; }
        public required string Status { get; set; }
        public int RetryCount { get; set; } = 0;

        public int AttemptCount { get; set; } = 0;
        public DateTime? LastAttemptAt { get; set; }
        public DateTime? NextRetryAt { get; set; }
        public string? FailureReason { get; set; }
    }
}
