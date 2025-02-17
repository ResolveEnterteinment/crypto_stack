using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domain.Models.Transaction
{
    public class TransactionData : BaseEntity
    {
        [BsonRepresentation(BsonType.ObjectId)]
        public required ObjectId UserId { get; set; }
        [BsonRepresentation(BsonType.ObjectId)]
        public required ObjectId SubscriptionId { get; set; }
        public required string PaymentProviderId { get; set; }
        public required decimal TotalAmount { get; set; }
        public required decimal PaymentProviderFee { get; set; }
        public required decimal PlatformFee { get; set; }
        public required decimal NetAmount { get; set; }
        public required string Status { get; set; }
    }
}
