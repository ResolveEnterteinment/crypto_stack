using Domain.Constants;

namespace Domain.Models.Crypto
{
    public class AssetData : BaseEntity
    {
        public required string Name { get; set; }
        public required string Ticker { get; set; }
        public required string Symbol { get; set; }
        public required uint Precision { get; set; }
        public string? SubunitName { get; set; }
        public string Class { get; set; } = AssetClass.Crypto;
    }
}
