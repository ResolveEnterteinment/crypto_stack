using Domain.DTOs.Payment;

namespace Application.Interfaces
{
    public interface IPaymentProvider
    {
        public string Name { get; }
        public Task<decimal> GetFeeAsync(string paymentIntentId);
        public Task<DateTime?> GetNextDueDate(string subscriptionId);
        public Task<Invoice> GetInvoiceAsync(string invoiceId);
        public Task<Subscription> GetSubscriptionAsync(string? subscriptionId);
        public Task<Subscription> GetSubscriptionByPaymentAsync(string paymentProviderId);
        public Task<string> CreateCheckoutSession(Guid userId, Guid subscriptionId, decimal amount, string interval);
    }
}