namespace Domain.DTOs.Subscription
{
    public class SubscriptionUpdateDto
    {
        public required IEnumerable<AllocationDto> Allocations { get; set; }
        public required string Interval { get; set; }
        public required int Amount { get; set; } = 0;
        public required DateTime NextDueDate { get; set; }
        public DateTime? EndDate { get; set; } = null;
    }
}
