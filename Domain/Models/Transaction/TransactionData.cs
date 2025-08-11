using Domain.Constants.Transaction;

namespace Domain.Models.Transaction
{
    public class TransactionData : BaseEntity
    {
        public required Guid UserId { get; set; }
        public string? PaymentProviderId { get; set; }
        public Guid? SubscriptionId { get; set; }
        public required Guid AssetId { get; set; }
        public required string SourceName { get; set; } = string.Empty;
        public required string SourceId { get; set; } = string.Empty;
        public required string Action { get; set; }
        public required decimal Quantity { get; set; } = 0m;
        public string? Description { get; set; } = string.Empty;
    }
}
