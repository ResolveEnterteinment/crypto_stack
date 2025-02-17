using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domain.Models.Subscription
{
    public class SubscriptionData : BaseEntity
    {
        [BsonRepresentation(BsonType.ObjectId)]
        public required ObjectId UserId { get; set; }
        public required IEnumerable<CoinAllocation> CoinAllocations { get; set; }
        public required string Interval { get; set; }
        public required int Amount { get; set; }
        public DateTime? EndDate { get; set; }
    }
}
