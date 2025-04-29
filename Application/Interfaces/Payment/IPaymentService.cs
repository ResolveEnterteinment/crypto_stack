using Application.Contracts.Requests.Payment;
using Domain.DTOs;
using Domain.DTOs.Payment;
using Domain.Models.Payment;

namespace Application.Interfaces.Payment
{
    public interface IPaymentService
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

        public Task<ResultWrapper<PaymentData>> GetByProviderIdAsync(string paymentProviderId);
    }
}
