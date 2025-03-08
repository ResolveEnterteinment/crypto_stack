using MediatR;
using MongoDB.Bson;

namespace Domain.Events
{
    // Event for MediatR
    public class RequestfundingEvent : BaseEvent, INotification
    {
        public decimal Amount { get; }
        public RequestfundingEvent(decimal amount, ObjectId storedEventId)
        {
            EventId = storedEventId;
            Amount = amount;
        }
    }
}