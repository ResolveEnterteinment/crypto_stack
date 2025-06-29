using System.ComponentModel.DataAnnotations;

namespace Application.Contracts.Requests.Subscription
{
    public class SubscriptionUpdateRequest
    {
        public IEnumerable<AllocationRequest>? Allocations { get; set; } = null;
        [Range(1, int.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
        public int? Amount { get; set; } = null;
        public DateTime? EndDate { get; set; } = null;
    }
}