using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domain.Models
{
    public class BaseEntity
    {
        [BsonRepresentation(BsonType.ObjectId)]
        [BsonId]
        [BsonElement(Order = 0)]
        public required ObjectId _id { get; set; }
        [BsonRepresentation(BsonType.DateTime)]
        [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
        [BsonElement(Order = 101)]
        public required DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
