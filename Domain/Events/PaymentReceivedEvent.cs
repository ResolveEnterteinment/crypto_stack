using MediatR;
using MongoDB.Bson;

namespace Domain.Events
{
    // Event for MediatR
    public class PaymentReceivedEvent : BaseEvent, INotification
    {
        public ObjectId PaymentId { get; }
        public PaymentReceivedEvent(ObjectId paymentId, ObjectId storedEventId)
        {
            EventId = storedEventId;
            PaymentId = paymentId;
        }
    }
}