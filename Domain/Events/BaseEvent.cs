using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domain.Events
{
    public class BaseEvent
    {
        public Guid EventId { get; set; }
        public Guid? TrailId { get; set; }
    }
}
