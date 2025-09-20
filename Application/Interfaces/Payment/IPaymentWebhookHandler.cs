using Domain.DTOs;

namespace Application.Interfaces.Payment
{
    /// <summary>
    /// Interface for handling payment provider webhook events
    /// </summary>
    public interface IPaymentWebhookHandler
    {
        /// <summary>
        /// Handles a Stripe event received from a webhook
        /// </summary>
        /// <param name="stripeEvent">The Stripe event object</param>
        /// <param name="correlationId">Correlation ID for tracing</param>
        /// <returns>True if the event was successfully handled</returns>
        Task<ResultWrapper> HandleStripeEventAsync(object stripeEvent, string? correlationId = null);
    }
}