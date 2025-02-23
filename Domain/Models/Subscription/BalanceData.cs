using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domain.Models.Subscription
{
    public class BalanceData
    {
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId CoinId { get; set; }
        public decimal Quantity { get; set; }
    }
}
