using Domain.Models;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domain.DTOs
{
    public class QueuedOrderData : BaseEntity
    {
        [BsonRepresentation(BsonType.ObjectId)]
        public required ObjectId UserId { get; set; }
        [BsonRepresentation(BsonType.ObjectId)]
        public required ObjectId TransactionId { get; set; }
        [BsonRepresentation(BsonType.ObjectId)]
        public required ObjectId CryptoId { get; set; }
        public required decimal QuoteQuantity { get; set; }
        public int RetryCount { get; set; } = 0;
        public ObjectId? PreviousOrderId { get; set; }
        public required string Status { get; set; }
    }
}
