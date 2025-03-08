using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domain.Models.Exchange
{
    public class ExchangeOrderData : BaseEntity
    {
        [BsonRepresentation(BsonType.ObjectId)]
        public required ObjectId UserId { get; set; }
        [BsonRepresentation(BsonType.ObjectId)]
        public required ObjectId PaymentId { get; set; }
        [BsonRepresentation(BsonType.ObjectId)]
        public required ObjectId SubscriptionId { get; set; }
        [BsonRepresentation(BsonType.ObjectId)]
        public required ObjectId AssetId { get; set; }
        public long? PlacedOrderId { get; set; }
        public required decimal QuoteQuantity { get; set; }
        public decimal? QuoteQuantityFilled { get; set; }
        public required decimal QuoteQuantityDust { get; set; }
        public decimal? Price { get; set; }
        public decimal? Quantity { get; set; }
        public int RetryCount { get; set; } = 0;
        public ObjectId? PreviousOrderId { get; set; }
        public required string Status { get; set; }
    }
}
