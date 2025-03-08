using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domain.Models.Transaction
{
    public class TransactionData : BaseEntity
    {
        [BsonRepresentation(BsonType.ObjectId)]
        public required ObjectId BalanceId { get; set; } = ObjectId.Empty;
        public required string SourceName { get; set; } = string.Empty;
        public required string SourceId { get; set; } = string.Empty;
        public required string Action { get; set; } = string.Empty;
        public required decimal Available { get; set; } = 0m;
        public required decimal Locked { get; set; } = 0m;
    }
}
