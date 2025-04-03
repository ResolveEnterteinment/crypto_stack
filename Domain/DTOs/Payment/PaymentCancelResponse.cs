// Domain/DTOs/Payment/PaymentCancelResponse.cs
namespace Domain.DTOs.Payment
{
    /// <summary>
    /// Response model for payment cancellation requests
    /// </summary>
    public class PaymentCancelResponse
    {
        /// <summary>
        /// Whether the operation was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// A message describing the result
        /// </summary>
        public string? Message { get; set; }
    }
}