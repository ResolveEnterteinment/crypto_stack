namespace Domain.DTOs
{
    public class SubscriptionCreateRequestDto
    {
        public List<AllocationDto> Allocations { get; set; }
        public string Interval { get; set; } // "one-time", "daily", "weekly", "monthly"
        public decimal Amount { get; set; }
        public DateTime? EndDate { get; set; }
    }
}
