using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domain.Models.Subscription
{
    public class SubscriptionData : BaseEntity
    {
        public required Guid UserId { get; set; }
        public required IEnumerable<AllocationData> Allocations { get; set; }
        public required string Interval { get; set; }
        public required int Amount { get; set; }
        public DateTime? EndDate { get; set; }
        public bool IsCancelled { get; set; }
    }
}
