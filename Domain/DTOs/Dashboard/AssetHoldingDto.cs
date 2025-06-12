namespace Domain.DTOs.Dashboard
{
    public class AssetHoldingDto
    {
        public required string Id { get; set; }
        public required string Name { get; set; }
        public required string Ticker { get; set; }
        public required string Symbol { get; set; }
        public decimal Available { get; set; }
        public decimal Locked { get; set; }
        public decimal Total { get; set; }
        public decimal Value { get; set; }
    }
}
