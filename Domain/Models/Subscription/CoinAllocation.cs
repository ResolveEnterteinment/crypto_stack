using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domain.Models.Subscription
{
    public class CoinAllocation
    {
        [BsonRepresentation(BsonType.ObjectId)]
        public required ObjectId CoinId { get; set; }
        public required uint PercentAmount { get; set; } // 0-100
    }
}
