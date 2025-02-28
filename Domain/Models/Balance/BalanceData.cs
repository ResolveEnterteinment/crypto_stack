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
        public ObjectId CoinId { get; set; }
        public decimal Quantity { get; set; }
        public IEnumerable<ObjectId> Orders { get; set; } = Enumerable.Empty<ObjectId>();
    }
}
