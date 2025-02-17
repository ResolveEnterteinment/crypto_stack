using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domain.Models
{
    public class BaseEntity
    {
        [BsonRepresentation(BsonType.ObjectId)]
        public required ObjectId _id { get; set; }
        public required DateTime CreateTime { get; set; }
    }
}
