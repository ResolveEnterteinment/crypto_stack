using Domain.Models.Payment;
using MediatR;

namespace Domain.Events
{
    // Event for MediatR
    public class PaymentReceivedEvent : BaseEvent, INotification
    {
        public PaymentData Payment { get; }
        public PaymentReceivedEvent(PaymentData payment, IDictionary<string, object?> context) :
            base(context)
        {
            Payment = payment;
            DomainEntityId = payment.Id;
        }
    }
}