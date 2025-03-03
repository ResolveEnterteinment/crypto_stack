using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domain.Models.Balance
{
    public class BalanceData : BaseEntity
    {
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId UserId { get; set; }
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId SubscriptionId { get; set; }
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId AssetId { get; set; }
        public decimal Available { get; set; } = decimal.Zero;
        public decimal Locked { get; set; } = decimal.Zero;
        public IEnumerable<ObjectId> Transactions { get; set; } = Enumerable.Empty<ObjectId>();
    }
}
