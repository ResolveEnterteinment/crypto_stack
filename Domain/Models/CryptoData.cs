namespace Domain.Models
{
    public class CryptoData : BaseEntity
    {
        public required string Name { get; set; }
        public required string Ticker { get; set; }
        public required string Symbol { get; set; }
        public required uint Precision { get; set; }
        public required string SubunitName { get; set; }
    }
}
