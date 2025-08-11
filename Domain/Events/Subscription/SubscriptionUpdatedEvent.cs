using Domain.DTOs.Event;
using MediatR;

namespace Domain.Events.Subscription
{
    // Event for MediatR
    public class SubscriptionUpdatedEvent : BaseEvent, INotification
    {
        public PaymentProviderEvent SubscriptionEvent;
        public SubscriptionUpdatedEvent(PaymentProviderEvent subscription, IDictionary<string, object?> context) :
            base(context)
        {
            SubscriptionEvent = subscription;
        }
    }
}