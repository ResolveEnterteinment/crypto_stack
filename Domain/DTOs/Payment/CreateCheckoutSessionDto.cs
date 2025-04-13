// Domain/DTOs/Payment/CreateCheckoutSessionDto.cs
namespace Domain.DTOs.Payment
{
    /// <summary>
    /// Data for creating a checkout session with a payment provider
    /// </summary>
    public class CreateCheckoutSessionDto
    {
        /// <summary>
        /// The name of the payment provider
        /// </summary>
        public string Provider { get; set; }
        /// <summary>
        /// The ID of the subscription to pay for
        /// </summary>
        public required string SubscriptionId { get; set; }

        /// <summary>
        /// The ID of the user making the payment
        /// </summary>
        public required string UserId { get; set; }

        public required string UserEmail { get; set; }

        /// <summary>
        /// The payment amount
        /// </summary>
        public required decimal Amount { get; set; }

        /// <summary>
        /// The currency code (e.g., USD)
        /// </summary>
        public required string Currency { get; set; }

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
        /// Additional metadata to include with the payment
        /// </summary>
        public Dictionary<string, string>? Metadata { get; set; }
    }
}