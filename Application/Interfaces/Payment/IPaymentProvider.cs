using Domain.DTOs;
using Domain.DTOs.Payment;

namespace Application.Interfaces
{
    public interface IPaymentProvider
    {
        /// <summary>
        /// Gets the name of the payment provider
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Gets the fee for a payment
        /// </summary>
        /// <param name="paymentIntentId">The payment intent ID</param>
        /// <returns>The fee amount</returns>
        Task<decimal> GetFeeAsync(string paymentIntentId);

        /// <summary>
        /// Gets the next due date for a subscription
        /// </summary>
        /// <param name="invoiceId">The invoice ID</param>
        /// <returns>The next due date</returns>
        Task<DateTime?> GetNextDueDate(string invoiceId);

        /// <summary>
        /// Creates a checkout session for a subscription
        /// </summary>
        /// <param name="userId">The ID of the user</param>
        /// <param name="subscriptionId">The ID of the subscription</param>
        /// <param name="amount">The payment amount</param>
        /// <param name="interval">The subscription interval (e.g., "monthly")</param>
        /// <returns>The URL to redirect the user to for checkout</returns>
        Task<ResultWrapper<SessionDto>> CreateCheckoutSession(Guid userId, Guid subscriptionId, decimal amount, string interval);

        public Task<PaymentSubscriptionDto> GetSubscriptionByPaymentAsync(string paymentProviderId);
        /// <summary>
        /// Creates a checkout session with detailed options
        /// </summary>
        /// <param name="options">The checkout session options</param>
        /// <returns>The checkout session</returns>
        Task<ResultWrapper<SessionDto>> CreateCheckoutSessionWithOptions(CheckoutSessionOptions options);

        Task<ResultWrapper> CancelPaymentAsync(string paymentId, string? reason = "requested_by_customer");
    }

    /// <summary>
    /// Options for creating a checkout session
    /// </summary>
    public class CheckoutSessionOptions
    {
        public string CustomerId { get; set; }
        public string SuccessUrl { get; set; }
        public string CancelUrl { get; set; }
        public string PaymentMethodType { get; set; } = "card";
        public string Mode { get; set; } = "payment";
        public List<SessionLineItem> LineItems { get; set; } = new();
        public Dictionary<string, string> Metadata { get; set; } = new();
    }

    /// <summary>
    /// Line item for a checkout session
    /// </summary>
    public class SessionLineItem
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public long Quantity { get; set; } = 1;
        public long UnitAmount { get; set; }
        public string Currency { get; set; } = "usd";
        public string Interval { get; set; }
    }

    /// <summary>
    /// Result of creating a checkout session
    /// </summary>
    public class CheckoutSessionResult
    {
        public string Id { get; set; }
        public string Url { get; set; }
        public string ClientSecret { get; set; }
        public string Status { get; set; }
    }
}