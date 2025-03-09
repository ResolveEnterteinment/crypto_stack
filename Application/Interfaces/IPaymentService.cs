using Application.Contracts.Requests.Payment;
using Domain.DTOs;
using Domain.Interfaces;
using Domain.Models.Payment;
using MongoDB.Bson;

namespace Application.Interfaces
{
    public interface IPaymentService : IRepository<PaymentData>
    {
        public Task<ResultWrapper<ObjectId>> ProcessChargeUpdatedEventAsync(ChargeRequest charge);
        public Task<ResultWrapper<ObjectId>> ProcessPaymentIntentSucceededEvent(PaymentIntentRequest request);
        public Task<decimal> GetFeeAsync(string paymentId);
    }
}
