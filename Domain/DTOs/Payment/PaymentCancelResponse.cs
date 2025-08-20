// Domain/DTOs/Payment/PaymentCancelResponse.cs
namespace Domain.DTOs.Payment
{
    /// <summary>
    /// Response model for payment cancellation requests
    /// </summary>
    public class PaymentCancelResponse
    {
        public string? PaymentId { get; set; }
        public string? Status { get; set; }
        public DateTime? CancelledAt { get; set; }
    }
}