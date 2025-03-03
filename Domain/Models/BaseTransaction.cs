using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domain.Models
{
    public class BaseTransaction : BaseEntity
    {
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId BalanceId { get; set; }
        public required string SourceName { get; set; }
        public required string SourceId { get; set; }
        public required string Action { get; set; }
        public decimal Available { get; set; }
        public decimal Locked { get; set; }
    }
}
