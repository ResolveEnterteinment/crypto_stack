namespace Domain.Models
{
    public class ExchangeOrderData : BaseEntity
    {
        public required string UserId { get; set; }
        public required string TranscationId { get; set; }
        public required long OrderId { get; set; }
        public required string CryptoId { get; set; }
        public required Decimal Quantity { get; set; }
        public required string Status { get; set; }
    }
}
