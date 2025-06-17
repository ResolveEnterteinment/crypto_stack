using Application.Contracts.Requests.Payment;
using Application.Interfaces.Base;
using Domain.DTOs;
using Domain.DTOs.Payment;
using Domain.Models.Payment;

namespace Application.Interfaces.Payment
{
    public interface IPaymentService : IBaseService<PaymentData>
    {
        public IReadOnlyDictionary<string, IPaymentProvider> Providers { get; }

        public Task<ResultWrapper> ProcessInvoicePaidEvent(InvoiceRequest invoice);
        /// <summary>
        /// Creates a checkout session with the payment provider
        /// </summary>
        /// <param name="request">The checkout session request details</param>
        /// <returns>The checkout session response with URL</returns>
        Task<ResultWrapper<SessionDto>> CreateCheckoutSessionAsync(CreateCheckoutSessionDto requestl);

        public Task<ResultWrapper> ProcessCheckoutSessionCompletedAsync(SessionDto checkoutSession);

        /// <summary>
        /// Gets the status of a payment
        /// </summary>
        /// <param name="paymentId">The payment ID</param>
        /// <returns>The payment status information</returns>
        Task<PaymentStatusResponse> GetPaymentStatusAsync(string paymentId);

        /// <summary>
        /// Gets detailed information about a payment
        /// </summary>
        /// <param name="paymentId">The payment ID</param>
        /// <returns>The payment details</returns>
        Task<PaymentDetailsDto> GetPaymentDetailsAsync(string paymentId);

        /// <summary>
        /// Cancels a pending payment
        /// </summary>
        /// <param name="paymentId">The payment ID</param>
        /// <returns>The result of the cancellation</returns>
        Task<ResultWrapper> CancelPaymentAsync(string paymentId);

        Task<ResultWrapper<PaymentData>> GetByProviderIdAsync(string paymentProviderId);

        Task<(decimal total, decimal fee, decimal platform, decimal net)> CalculatePaymentAmounts(InvoiceRequest i);
        Task<ResultWrapper> ProcessPaymentFailedAsync(PaymentIntentRequest paymentIntentRequest);

        // Update Application/Interfaces/Payment/IPaymentService.cs to add:
        Task<ResultWrapper> UpdatePaymentRetryInfoAsync(
            Guid paymentId,
            int attemptCount,
            DateTime lastAttemptAt,
            DateTime nextRetryAt,
            string failureReason);

        Task<ResultWrapper<IEnumerable<PaymentData>>> GetPendingRetriesAsync();

        Task<ResultWrapper> RetryPaymentAsync(Guid paymentId);

        Task<ResultWrapper<string>> CreateUpdatePaymentMethodSessionAsync(string userId, string subscriptionId);

        Task<ResultWrapper> ProcessSetupIntentSucceededAsync(Guid subscriptionId);

        Task<ResultWrapper<IEnumerable<PaymentData>>> GetPaymentsForSubscriptionAsync(Guid subscriptionId);
        Task<ResultWrapper<int>> GetFailedPaymentCountAsync(Guid subscriptionId);
        Task<ResultWrapper<PaymentData>> GetLatestPaymentAsync(Guid subscriptionId);

        /// <summary>
        /// Fetches payment records from Stripe for a subscription and processes any missing records
        /// </summary>
        /// <param name="stripeSubscriptionId">The Stripe subscription ID</param>
        /// <returns>Number of missing payment records that were processed</returns>
        Task<ResultWrapper<int>> FetchPaymentsBySubscriptionAsync(string stripeSubscriptionId);

        /// <summary>
        /// Searches for Stripe subscriptions by metadata
        /// </summary>
        /// <param name="metadataKey">The metadata key to search for</param>
        /// <param name="metadataValue">The metadata value to search for</param>
        /// <returns>The first matching Stripe subscription ID, or null if not found</returns>
        Task<ResultWrapper<string?>> SearchStripeSubscriptionByMetadataAsync(string metadataKey, string metadataValue);
    }
}
