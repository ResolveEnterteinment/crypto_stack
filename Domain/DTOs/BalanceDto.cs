using Domain.Models.Crypto;
using MongoDB.Bson.Serialization.Attributes;

namespace Domain.DTOs
{
    public class BalanceDto
    {
        public string AssetName { get; set; }
        public string Ticker { get; set; }
        public string Symbol { get; set; }
        public decimal Available { get; set; }
        public decimal Locked { get; set; }
        public decimal Total { get; set; }

        [BsonIgnore] // Used for lookup results
        public AssetData AssetDocs { get; set; }
    }
}
