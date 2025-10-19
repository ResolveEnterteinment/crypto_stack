using Domain.Models.Asset;

namespace Domain.DTOs.Asset
{
    public class AssetDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Ticker { get; set; }
        public string Symbol { get; set; }
        public int Precision { get; set; }
        public string? SubunitName { get; set; }
        public string Type { get; set; }
        public string Class { get; set; }
        public AssetDto(AssetData asset)
        {
            Id = asset.Id;
            Name = asset.Name;
            Ticker = asset.Ticker;
            Symbol = asset.Symbol;
            Precision = asset.Precision;
            SubunitName = asset.SubunitName;
            Type = asset.Type;
            Class = asset.Class;
        }
    }
}
