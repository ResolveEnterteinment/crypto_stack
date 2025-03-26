using Domain.DTOs;
using Domain.DTOs.Exchange;
using Domain.Models.Payment;

namespace Application.Interfaces.Exchange
{
    public interface IPaymentProcessingService
    {
        public Task<ResultWrapper<IEnumerable<OrderResult>>> ProcessPayment(PaymentData transactionData);
    }
}
