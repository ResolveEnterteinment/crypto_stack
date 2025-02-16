using MongoDB.Bson;

namespace Domain.Models.Exchange
{
    public class ExchangeOrderData : BaseEntity
    {
        public required ObjectId UserId { get; set; }
        public required ObjectId TranscationId { get; set; }
        public required long? OrderId { get; set; }
        public required ObjectId CryptoId { get; set; }
        public required decimal QuoteQuantity { get; set; }
        public required decimal? Price { get; set; }
        public required decimal Quantity { get; set; }
        public required string Status { get; set; }
    }
}
