namespace Domain.Models.Crypto
{
    public class CoinData : BaseEntity
    {
        public required string Name { get; set; }
        public required string Ticker { get; set; }
        public required string Symbol { get; set; }
        public required uint Precision { get; set; }
        public required string SubunitName { get; set; }
    }
}
