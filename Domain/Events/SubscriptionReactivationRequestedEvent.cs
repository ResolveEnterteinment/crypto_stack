using MediatR;

namespace Domain.Events
{
    public class SubscriptionReactivationRequestedEvent : BaseEvent, INotification
    {
        public Guid SubscriptionId { get; }

        public SubscriptionReactivationRequestedEvent(Guid subscriptionId, IDictionary<string, object?> context = null)
            : base(context)
        {
            SubscriptionId = subscriptionId;
        }
    }
}