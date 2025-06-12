namespace Domain.Models.Asset
{
    public class AssetData : BaseEntity
    {
        public required string Name { get; set; }
        public required string Ticker { get; set; }
        public required string Symbol { get; set; }
        public required uint Precision { get; set; }
        public string? SubunitName { get; set; }
        public required string Exchange { get; set; }
        public required string Type { get; set; }
        public required string Class { get; set; }
    }
}
