// Domain/DTOs/Payment/PaymentStatusResponse.cs
namespace Domain.DTOs.Payment
{
    /// <summary>
    /// Response model for payment status requests
    /// </summary>
    public class PaymentStatusResponse
    {
        /// <summary>
        /// The payment ID
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// The current status of the payment
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// The payment amount
        /// </summary>
        public decimal Amount { get; set; }

        /// <summary>
        /// The currency code
        /// </summary>
        public string Currency { get; set; }

        /// <summary>
        /// The ID of the subscription this payment is for
        /// </summary>
        public string SubscriptionId { get; set; }

        /// <summary>
        /// When the payment was created
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// When the payment was last updated
        /// </summary>
        public DateTime UpdatedAt { get; set; }
    }
}