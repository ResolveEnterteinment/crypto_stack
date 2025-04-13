using Domain.DTOs.Event;
using MediatR;

namespace Domain.Events
{
    // Event for MediatR
    public class SubscriptionUpdatedEvent : BaseEvent, INotification
    {
        public PaymentProviderEvent SubscriptionEvent;
        public SubscriptionUpdatedEvent(PaymentProviderEvent subscription)
        {
            SubscriptionEvent = subscription;
        }
    }
}