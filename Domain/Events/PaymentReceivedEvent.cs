using Domain.Models.Payment;
using MediatR;
using MongoDB.Bson;

namespace Domain.Events
{
    // Event for MediatR
    public class PaymentReceivedEvent : BaseEvent, INotification
    {
        public PaymentData Payment { get; }
        public PaymentReceivedEvent(PaymentData payment, Guid storedEventId)
        {
            EventId = storedEventId;
            Payment = payment;
        }
    }
}