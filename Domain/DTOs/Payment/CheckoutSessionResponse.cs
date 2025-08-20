// Domain/DTOs/Payment/CheckoutSessionResponse.cs
namespace Domain.DTOs.Payment
{
    /// <summary>
    /// Response model for checkout session creation
    /// </summary>
    public class CheckoutSessionResponse
    {

        /// <summary>
        /// URL to redirect the user to for checkout
        /// </summary>
        public string? CheckoutUrl { get; set; }

        /// <summary>
        /// Client secret for Stripe Elements integration (if applicable)
        /// </summary>
        public string? ClientSecret { get; set; }

        /// <summary>
        /// Gets or sets the unique identifier for the current session.
        /// </summary>
        public string? SessionId { get; set; }
    }
}