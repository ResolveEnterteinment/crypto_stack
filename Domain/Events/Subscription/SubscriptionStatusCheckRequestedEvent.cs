namespace Domain.Events.Subscription
{
    public class SubscriptionStatusCheckRequestedEvent : BaseEvent
    {
        public Guid SubscriptionId { get; }
        public string Status { get; }

        public SubscriptionStatusCheckRequestedEvent(Guid subscriptionId, string status, IDictionary<string, object?> context = null)
            : base(context)
        {
            SubscriptionId = subscriptionId;
            Status = status;
        }
    }
}