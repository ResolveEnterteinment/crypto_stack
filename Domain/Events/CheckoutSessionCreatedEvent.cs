using Domain.DTOs.Payment;
using MediatR;

namespace Domain.Events
{
    // Event for MediatR
    public class CheckoutSessionCreatedEvent : BaseEvent, INotification
    {
        public SessionDto Session { get; }
        public CheckoutSessionCreatedEvent(SessionDto session)
        {
            Session = session;
            if (session.Metadata.TryGetValue("subscriptionId", out var subscriptionIdString))
            {
                if (Guid.TryParse(subscriptionIdString, out var subscriptionId))
                {
                    DomainEntityId = subscriptionId;
                }
            }
        }
    }
}