using Domain.DTOs.Payment;
using MediatR;

namespace Domain.Events
{
    // Event for MediatR
    public class CheckoutSessionCreatedEvent : BaseEvent, INotification
    {
        public SessionDto Session { get; }
        public CheckoutSessionCreatedEvent(SessionDto session, Guid storedEventId)
        {
            EventId = storedEventId;
            Session = session;
        }
    }
}