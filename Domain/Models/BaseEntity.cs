using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domain.Models
{
    public class BaseEntity
    {
        public Guid Id { get; set; }
        [BsonRepresentation(BsonType.DateTime)]
        public DateTime CreateTime { get; set; } = DateTime.UtcNow;
    }
}
