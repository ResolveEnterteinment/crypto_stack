using Domain.Attributes;
using Domain.Models.Asset;
using MongoDB.Bson.Serialization.Attributes;

namespace Domain.Models.Balance
{
    [BsonCollection("balances")]
    public class BalanceData : BaseEntity
    {
        public Guid UserId { get; set; }
        public Guid AssetId { get; set; }
        public string? Ticker { get; set; }
        public decimal Available { get; set; } = decimal.Zero;
        public decimal Locked { get; set; } = decimal.Zero;
        public decimal Total { get; set; } = decimal.Zero;
        public Guid LastTransactionId { get; set; }
        [BsonIgnore]
        public AssetData? Asset { get; set; }
    }
}
