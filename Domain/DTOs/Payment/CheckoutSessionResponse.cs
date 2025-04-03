// Domain/DTOs/Payment/CheckoutSessionResponse.cs
namespace Domain.DTOs.Payment
{
    /// <summary>
    /// Response model for checkout session creation
    /// </summary>
    public class CheckoutSessionResponse
    {
        /// <summary>
        /// Whether the operation was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// A message describing the result
        /// </summary>
        public string? Message { get; set; }

        /// <summary>
        /// URL to redirect the user to for checkout
        /// </summary>
        public string? CheckoutUrl { get; set; }

        /// <summary>
        /// Client secret for Stripe Elements integration (if applicable)
        /// </summary>
        public string? ClientSecret { get; set; }
    }
}