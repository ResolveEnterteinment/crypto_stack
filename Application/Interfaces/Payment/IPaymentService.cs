using Application.Contracts.Requests.Payment;
using Domain.DTOs;
using Domain.DTOs.Payment;

namespace Application.Interfaces.Payment
{
    public interface IPaymentService : IRepository<Domain.Models.Payment.PaymentData>
    {
        public Dictionary<string, IPaymentProvider> Providers { get; }

        public Task<ResultWrapper<Guid>> ProcessChargeUpdatedEventAsync(ChargeRequest charge);
        public Task<ResultWrapper<Guid>> ProcessPaymentIntentSucceededEvent(PaymentIntentRequest request);
        /// <summary>
        /// Creates a checkout session with the payment provider
        /// </summary>
        /// <param name="request">The checkout session request details</param>
        /// <returns>The checkout session response with URL</returns>
        Task<CheckoutSessionResponse> CreateCheckoutSessionAsync(CreateCheckoutSessionDto request, string? correlationId = null);

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
    }
}
