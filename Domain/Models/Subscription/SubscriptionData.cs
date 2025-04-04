using Domain.Constants;

namespace Domain.Models.Subscription
{
    public class SubscriptionData : BaseEntity
    {
        public string? Provider { get; set; }
        public string? ProviderSubscriptionId { get; set; } = null;
        public required Guid UserId { get; set; }
        public required IEnumerable<AllocationData> Allocations { get; set; }
        public required string Interval { get; set; }
        public required int Amount { get; set; } = 0;
        public required string Currency { get; set; }
        public DateTime? NextDueDate { get; set; }
        public decimal? TotalInvestments { get; set; }
        public DateTime? EndDate { get; set; } = null;
        public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Pending;
        public bool IsCancelled { get; set; } = false;
    }
}
