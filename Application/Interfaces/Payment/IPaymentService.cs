using Application.Contracts.Requests.Payment;
using Domain.DTOs;
using Domain.Interfaces;
using Domain.Models.Payment;

namespace Application.Interfaces.Payment
{
    public interface IPaymentService : IRepository<PaymentData>
    {
        public Dictionary<string, IPaymentProvider> Providers { get; }
        public Task<ResultWrapper<Guid>> ProcessChargeUpdatedEventAsync(ChargeRequest charge);
        public Task<ResultWrapper<Guid>> ProcessPaymentIntentSucceededEvent(PaymentIntentRequest request);
    }
}
