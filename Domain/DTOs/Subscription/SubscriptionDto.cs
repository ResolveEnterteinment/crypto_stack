using Domain.Constants.Subscription;

namespace Domain.DTOs.Subscription
{
    public class SubscriptionDto
    {
        public required Guid Id { get; set; }
        public required DateTime CreatedAt { get; set; }
        public required IEnumerable<AllocationDto> Allocations { get; set; }
        public required string Interval { get; set; }
        public required int Amount { get; set; } = 0;
        public required string Currency { get; set; }
        public required DateTime? LastPayment { get; set; }
        public required DateTime NextDueDate { get; set; }
        public required decimal TotalInvestments { get; set; } = 0;
        public DateTime? EndDate { get; set; } = null;
        public required string Status { get; set; } = SubscriptionStatus.Pending;
        public required string State { get; set; } = SubscriptionState.Idle;
        public bool IsCancelled { get; set; } = false;
    }
}
