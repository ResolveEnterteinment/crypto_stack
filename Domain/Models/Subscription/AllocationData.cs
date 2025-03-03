using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domain.Models.Subscription
{
    public class AllocationData
    {
        [BsonRepresentation(BsonType.ObjectId)]
        public required ObjectId AssetId { get; set; }
        public required uint PercentAmount { get; set; } // 0-100
    }
}
