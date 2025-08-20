using Domain.Models.Payment;

namespace Domain.DTOs.Payment
{
    public class PaymentDto
    {
        public Guid Id { get; set; }
        public Guid SubscriptionId { get; set; }
        public string Status { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal NetAmount { get; set; }
        public string Currency { get; set; }
        public DateTime CreatedAt { get; set; }
        public int? AttemptCount { get; set; }
        public DateTime? LastAttemptAt { get; set; }
        public DateTime? NextRetryAt { get; set; }
        public string? FailureReason { get; set; }

        public PaymentDto() { }

        public PaymentDto(PaymentData payment)
        {
            Id = payment.Id;
            SubscriptionId = payment.SubscriptionId;
            Status = payment.Status;
            TotalAmount = payment.TotalAmount;
            NetAmount = payment.NetAmount;
            Currency = payment.Currency;
            CreatedAt = payment.CreatedAt;
            AttemptCount = payment.AttemptCount;
            LastAttemptAt = payment.LastAttemptAt;
            NextRetryAt = payment.NextRetryAt;
            FailureReason = payment.FailureReason;
        }
    }
}