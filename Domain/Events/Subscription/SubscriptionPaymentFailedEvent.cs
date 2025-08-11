// Domain/Events/SubscriptionPaymentFailedEvent.cs
using Domain.Models.Payment;
using MediatR;

namespace Domain.Events.Subscription
{
    public class SubscriptionPaymentFailedEvent : BaseEvent, INotification
    {
        public PaymentData Payment { get; }
        public string FailureReason { get; }
        public int AttemptCount { get; }

        public SubscriptionPaymentFailedEvent(
            PaymentData payment,
            string failureReason,
            int attemptCount,
            IDictionary<string, object?> context = null) : base(context)
        {
            Payment = payment;
            FailureReason = failureReason;
            AttemptCount = attemptCount;
            DomainEntityId = payment.Id;
        }
    }
}