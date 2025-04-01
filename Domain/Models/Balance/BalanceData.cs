using Domain.Models.Asset;
using MongoDB.Bson.Serialization.Attributes;

namespace Domain.Models.Balance
{
    public class BalanceData : BaseEntity
    {
        public Guid UserId { get; set; }
        public Guid AssetId { get; set; }
        public string Ticker { get; set; }
        public decimal Available { get; set; } = decimal.Zero;
        public decimal Locked { get; set; } = decimal.Zero;
        public decimal Total { get; set; } = decimal.Zero;
        public DateTime LastUpdated { get; set; }
        [BsonIgnore]
        public AssetData AssetDocs { get; set; }
    }
}
