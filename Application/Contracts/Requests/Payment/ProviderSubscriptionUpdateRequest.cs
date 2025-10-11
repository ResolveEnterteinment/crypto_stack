using Domain.Constants.Subscription;

namespace Application.Contracts.Requests.Payment
{
    public class ProviderSubscriptionUpdateRequest
    {
        public required string Interval { get; set; }
        public required int Amount { get; set; } = 0;
        public required DateTime NextDueDate { get; set; }
        public DateTime? EndDate { get; set; } = null;
        public required string Status { get; set; } = SubscriptionStatus.Pending;
        public bool IsCancelled { get; set; } = false;
    }
}