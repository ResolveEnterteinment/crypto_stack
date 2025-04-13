using Domain.DTOs.Payment;
using MediatR;

namespace Domain.Events
{
    // Event for MediatR
    public class CheckoutSessionCompletedEvent : BaseEvent, INotification
    {
        public SessionDto Session { get; }
        public CheckoutSessionCompletedEvent(SessionDto session)
        {
            Session = session;
            if (session.Metadata.TryGetValue("subscriptionId", out var subscriptionIdString))
            {
                if (Guid.TryParse(subscriptionIdString, out var subscriptionId))
                {
                    DomainRecordId = subscriptionId;
                }
            }
        }
    }
}