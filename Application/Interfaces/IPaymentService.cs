using Application.Contracts.Requests.Payment;
using Domain.DTOs;
using Domain.Interfaces;
using Domain.Models.Payment;
using MongoDB.Bson;

namespace Application.Interfaces
{
    public interface IPaymentService : IRepository<PaymentData>
    {
        public Task<ResultWrapper<Guid>> ProcessChargeUpdatedEventAsync(ChargeRequest charge);
        public Task<ResultWrapper<Guid>> ProcessPaymentIntentSucceededEvent(PaymentIntentRequest request);
        public Task<decimal> GetFeeAsync(string paymentId);
    }
}
