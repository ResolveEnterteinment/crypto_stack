using Domain.DTOs.Payment;
using MediatR;

namespace Domain.Events.Payment
{
    // Event for MediatR
    public class CheckoutSessionCompletedEvent : BaseEvent, INotification
    {
        public SessionDto Session { get; }
        public CheckoutSessionCompletedEvent(SessionDto session, IDictionary<string, object?> context) :
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