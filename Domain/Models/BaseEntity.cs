using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domain.Models
{
    public class BaseEntity
    {
        [BsonRepresentation(BsonType.ObjectId)]
        public required ObjectId _id { get; set; }
        [BsonRepresentation(BsonType.DateTime)]
        public required DateTime CreateTime { get; set; } = DateTime.UtcNow;
    }
}
