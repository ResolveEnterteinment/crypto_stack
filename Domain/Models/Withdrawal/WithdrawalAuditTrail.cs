namespace Domain.Models.Withdrawal
{
    public class WithdrawalAuditTrail
    {
        public required string OldStatus { get; set; }
        public required string NewStatus { get; set; }
        public required DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string? Comment { get; set; }
        public required Guid ProcessedBy;
    }
}
