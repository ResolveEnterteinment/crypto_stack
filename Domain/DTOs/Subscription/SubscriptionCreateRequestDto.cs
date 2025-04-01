namespace Domain.DTOs.Subscription
{
    public class SubscriptionCreateRequestDto
    {
        public List<AllocationCreateDto> Allocations { get; set; }
        public string Interval { get; set; } // "one-time", "daily", "weekly", "monthly"
        public decimal Amount { get; set; }
        public DateTime? EndDate { get; set; }
    }
}
