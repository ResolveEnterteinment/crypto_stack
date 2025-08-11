using Domain.Models.Payment;
using MediatR;

namespace Domain.Events.Payment
{
    // Event for MediatR
    public class PaymentCancelledEvent : BaseEvent, INotification
    {
        public PaymentData Payment { get; }
        public PaymentCancelledEvent(PaymentData payment, IDictionary<string, object?> context) :
            base(context)
        {
            Payment = payment;
            DomainEntityId = payment.Id;
        }
    }
}