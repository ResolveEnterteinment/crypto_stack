// Domain/DTOs/Payment/CheckoutSessionRequest.cs
namespace Domain.DTOs.Payment
{
    /// <summary>
    /// Request model for creating a checkout session
    /// </summary>
    public class CheckoutSessionRequest
    {
        /// <summary>
        /// The ID of the subscription to pay for
        /// </summary>
        public required string SubscriptionId { get; set; }

        /// <summary>
        /// The ID of the user making the payment
        /// </summary>
        public required string UserId { get; set; }

        /// <summary>
        /// The payment amount
        /// </summary>
        public required decimal Amount { get; set; }

        /// <summary>
        /// The currency code (e.g., USD)
        /// </summary>
        public string? Currency { get; set; } = "USD";

        /// <summary>
        /// Whether this is a recurring subscription
        /// </summary>
        public bool IsRecurring { get; set; }

        public string Interval { get; set; }

        /// <summary>
        /// URL to return to after successful payment
        /// </summary>
        public string? ReturnUrl { get; set; }

        /// <summary>
        /// URL to return to if payment is cancelled
        /// </summary>
        public string? CancelUrl { get; set; }

        /// <summary>
        /// Idempotency key to prevent duplicate operations
        /// </summary>
        public string? IdempotencyKey { get; set; }
    }
}