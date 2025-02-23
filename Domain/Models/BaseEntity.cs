using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domain.Models
{
    public class BaseEntity
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId _id { get; set; }
        [BsonRepresentation(BsonType.DateTime)]
        public DateTime CreateTime { get; set; } = DateTime.UtcNow;
    }
}
