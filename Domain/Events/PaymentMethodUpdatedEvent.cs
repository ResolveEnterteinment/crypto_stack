using MediatR;

namespace Domain.Events
{
    public class PaymentMethodUpdatedEvent : BaseEvent, INotification
    {
        public Guid SubscriptionId { get; }
        public string UserId { get; }

        public PaymentMethodUpdatedEvent(Guid subscriptionId, string userId, IDictionary<string, object?> context = null)
            : base(context)
        {
            SubscriptionId = subscriptionId;
            UserId = userId;
        }
    }
}