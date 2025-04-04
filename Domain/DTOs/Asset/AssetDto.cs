namespace Domain.DTOs.Asset
{
    public class AssetDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Ticker { get; set; }
        public string Symbol { get; set; }
        public uint Precision { get; set; }
        public string? SubunitName { get; set; }
    }
}
