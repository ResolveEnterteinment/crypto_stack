using MediatR;

namespace Domain.Events.Exchange
{
    // Event for MediatR
    public class RequestFundingEvent : BaseEvent, INotification
    {
        public decimal Amount { get; }
        public RequestFundingEvent(decimal amount, Guid storedEventId, IDictionary<string, object?> context) :
            base(context)
        {
            Amount = amount;
        }
    }
}