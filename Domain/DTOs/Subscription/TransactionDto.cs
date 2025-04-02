namespace Domain.DTOs.Subscription
{
    public class TransactionDto
    {
        public string Action { get; set; }
        public string AssetName { get; set; }
        public string AssetTicker { get; set; }
        public decimal Quantity { get; set; }
        public DateTime CreatedAt { get; set; }
        public decimal QuoteQuantity { get; set; }
        public string QuoteCurrency { get; set; }
    }
}
