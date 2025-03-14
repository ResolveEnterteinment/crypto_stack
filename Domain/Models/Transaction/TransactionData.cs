namespace Domain.Models.Transaction
{
    public class TransactionData : BaseEntity
    {
        public required Guid UserId { get; set; }
        public string PaymentProviderId { get; set; }
        public Guid SubscriptionId { get; set; }
        public required Guid BalanceId { get; set; } = Guid.Empty;
        public required string SourceName { get; set; } = string.Empty;
        public required string SourceId { get; set; } = string.Empty;
        public required string Action { get; set; } = string.Empty;
        public required decimal Quantity { get; set; } = 0m;
    }
}
