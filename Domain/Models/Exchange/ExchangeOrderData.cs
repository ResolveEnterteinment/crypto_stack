using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domain.Models.Exchange
{
    public class ExchangeOrderData : BaseEntity
    {
        public required Guid UserId { get; set; }
        public required Guid PaymentId { get; set; }
        public required Guid SubscriptionId { get; set; }
        public required Guid AssetId { get; set; }
        public long? PlacedOrderId { get; set; }
        public required decimal QuoteQuantity { get; set; }
        public decimal? QuoteQuantityFilled { get; set; }
        public required decimal QuoteQuantityDust { get; set; }
        public decimal? Price { get; set; }
        public decimal? Quantity { get; set; }
        public int RetryCount { get; set; } = 0;
        public Guid? PreviousOrderId { get; set; }
        public required string Status { get; set; }
    }
}
