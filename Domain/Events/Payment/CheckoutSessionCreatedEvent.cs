using Domain.DTOs.Payment;
using MediatR;

namespace Domain.Events.Payment
{
    // Event for MediatR
    public class CheckoutSessionCreatedEvent : BaseEvent, INotification
    {
        public SessionDto Session { get; }
        public CheckoutSessionCreatedEvent(SessionDto session, IDictionary<string, object?> context) :
            base(context)
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