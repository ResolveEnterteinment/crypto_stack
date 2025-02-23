using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domain.DTOs
{
    public class PlacedOrderResult
    {
        public required long OrderId { get; set; }
        [BsonRepresentation(BsonType.ObjectId)]
        public required ObjectId CryptoId { get; set; }
        public required decimal QuoteQuantity { get; set; }
        public required decimal? Price { get; set; }
        public required decimal Quantity { get; set; }
        public required string Status { get; set; }
    }
}
