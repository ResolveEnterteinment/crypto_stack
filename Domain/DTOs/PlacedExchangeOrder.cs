using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domain.DTOs
{
    public class PlacedExchangeOrder
    {
        public required long OrderId { get; set; }
        [BsonRepresentation(BsonType.ObjectId)]
        public required string Symbol { get; set; }
        public required decimal QuoteQuantity { get; set; }
        public required decimal QuoteQuantityFilled { get; set; }
        public required decimal? Price { get; set; }
        public required decimal QuantityFilled { get; set; }
        public required string Status { get; set; }
    }
}
