namespace Domain.DTOs.Subscription
{
    public class SubscriptionCreateRequestDto
    {
        public required Guid UserId { get; set; }
        public List<AllocationCreateDto> Allocations { get; set; }
        public string Interval { get; set; } // "one-time", "daily", "weekly", "monthly"
        public decimal Amount { get; set; }
        public required string Currency { get; set; }
        public DateTime? EndDate { get; set; }
    }
}
