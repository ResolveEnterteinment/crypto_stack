using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domain.Models.Subscription
{
    public class AllocationData
    {
        public required Guid AssetId { get; set; }
        public required string AssetTicker { get; set; }
        public required uint PercentAmount { get; set; } // 0-100
    }
}
