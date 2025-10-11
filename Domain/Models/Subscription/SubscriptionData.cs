using Domain.Constants.Subscription;

namespace Domain.Models.Subscription
{
    public class SubscriptionData : BaseEntity
    {
        public string Provider { get; set; }
        public string ProviderSubscriptionId { get; set; }
        public required Guid UserId { get; set; }
        public required IEnumerable<AllocationData> Allocations { get; set; }
        public required string Interval { get; set; }
        public required int Amount { get; set; } = 0;
        public required string Currency { get; set; }
        public DateTime? LastPayment { get; set; }
        public DateTime? NextDueDate { get; set; }
        public decimal? TotalInvestments { get; set; } = 0m;
        public DateTime? EndDate { get; set; } = null;
        public string Status { get; set; } = SubscriptionStatus.Pending;
        public string? State { get; set; } = SubscriptionState.Idle;
        public List<string> ProcessingPayments { get; set; } = [];
        public bool HasProcessingPayment => ProcessingPayments.Count > 0;
        public List<string> AcquiringAssets { get; set; } = [];
        public bool HasAcquiringAssets => AcquiringAssets.Count > 0;
        public bool IsCancelled { get; set; } = false;
    }
}
