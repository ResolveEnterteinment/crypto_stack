using Domain.Models.Payment;
using MediatR;

namespace Domain.Events
{
    // Event for MediatR
    public class PaymentCancelledEvent : BaseEvent, INotification
    {
        public PaymentData Payment { get; }
        public PaymentCancelledEvent(PaymentData payment)
        {
            Payment = payment;
            DomainEntityId = payment.Id;
        }
    }
}