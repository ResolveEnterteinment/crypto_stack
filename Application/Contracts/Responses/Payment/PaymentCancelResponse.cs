namespace Application.Contracts.Responses.Payment
{
    /// <summary>
    /// Response model for payment cancellation requests
    /// </summary>
    public class PaymentCancelResponse
    {
        public string PaymentId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CancelledAt { get; set; }
    }
}
