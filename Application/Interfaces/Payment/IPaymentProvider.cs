using Domain.DTOs;
using Domain.DTOs.Payment;

namespace Application.Interfaces
{
    public interface IPaymentProvider
    {
        public Task<decimal> GetFeeAsync(string paymentIntentId);
        public Task<DateTime?> GetNextDueDate(string subscriptionId);
        public Task<Invoice> GetInvoiceAsync(string invoiceId);
        public Task<Subscription> GetSubscriptionAsync(string? subscriptionId);
        public Task<Domain.DTOs.Payment.Subscription> GetSubscriptionByPaymentAsync(string paymentProviderId);
    }
}