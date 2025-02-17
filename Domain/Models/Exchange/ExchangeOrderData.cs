using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domain.Models.Exchange
{
    public class ExchangeOrderData : BaseEntity
    {
        [BsonRepresentation(BsonType.ObjectId)]
        public required ObjectId UserId { get; set; }
        [BsonRepresentation(BsonType.ObjectId)]
        public required ObjectId TranscationId { get; set; }
        public required long? OrderId { get; set; }
        [BsonRepresentation(BsonType.ObjectId)]
        public required ObjectId CryptoId { get; set; }
        public required decimal QuoteQuantity { get; set; }
        public required decimal? Price { get; set; }
        public required decimal Quantity { get; set; }
        public required string Status { get; set; }
    }
}
