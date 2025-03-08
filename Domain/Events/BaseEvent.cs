using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domain.Events
{
    public class BaseEvent
    {
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId EventId { get; set; }
        public ObjectId? TrailId { get; set; }
    }
}
