namespace Domain.DTOs.Exchange
{
    public class PlacedExchangeOrder
    {
        public required string Exchange { get; set; }
        public required string Side { get; set; }
        public required long OrderId { get; set; }
        public required string Symbol { get; set; }
        public required decimal QuoteQuantity { get; set; }
        public required decimal QuoteQuantityFilled { get; set; }
        public required decimal? Price { get; set; }
        public required decimal QuantityFilled { get; set; }
        public required string Status { get; set; }
    }
}
