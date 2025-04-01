namespace Domain.DTOs.Dashboard
{
    public class AssetHoldingsDto
    {
        public string Name { get; set; }
        public string Ticker { get; set; }
        public string Symbol { get; set; }
        public decimal Available { get; set; }
        public decimal Locked { get; set; }
        public decimal Total { get; set; }
        public decimal Value { get; set; }
    }
}
