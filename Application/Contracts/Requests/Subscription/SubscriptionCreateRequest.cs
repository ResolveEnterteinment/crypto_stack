using System.ComponentModel.DataAnnotations;

namespace Application.Contracts.Requests.Subscription
{
    public class SubscriptionCreateRequest
    {
        [Required(ErrorMessage = "UserId is required.")]
        public required string UserId { get; set; }
        [Required(ErrorMessage = "Allocations are required."), MinLength(1)]
        public required IEnumerable<AllocationRequest> Allocations { get; set; }
        [Required(ErrorMessage = "Interval is required.")]
        public required string Interval { get; set; }
        [Range(1, int.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
        public required int Amount { get; set; } = 0;
        public required string Currency { get; set; }
        public DateTime? EndDate { get; set; } = null;
    }

    public class AllocationRequest
    {
        [Required(ErrorMessage = "AssetId is required.")]
        public required string AssetId { get; set; }

        [Range(0, 100, ErrorMessage = "PercentAmount must be between 0 and 100.")]
        public required int PercentAmount { get; set; }
    }
}