using MediatR;

namespace Domain.Events
{
    // Event for MediatR
    public class RequestfundingEvent : BaseEvent, INotification
    {
        public decimal Amount { get; }
        public RequestfundingEvent(decimal amount, Guid storedEventId, IDictionary<string, object?> context) :
            base(context)
        {
            EventId = storedEventId;
            Amount = amount;
        }
    }
}