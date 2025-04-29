using Domain.DTOs.Event;
using MediatR;

namespace Domain.Events
{
    // Event for MediatR
    public class SubscriptionCreatedEvent : BaseEvent, INotification
    {
        public PaymentProviderEvent Subscription;

        public DateTime CurrentPeriodEnd;
        public SubscriptionCreatedEvent(PaymentProviderEvent subscription, IDictionary<string, object?> context) :
            base(context)
        {
            Subscription = subscription;
        }
    }
}