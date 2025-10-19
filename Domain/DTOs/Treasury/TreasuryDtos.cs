namespace Domain.DTOs.Treasury
{
    /// <summary>
    /// Metadata for creating treasury transactions
    /// </summary>
    public class TreasuryTransactionMetadata
    {
        public Guid? UserId { get; set; }
        public Guid? SubscriptionId { get; set; }
        public string? RelatedTransactionId { get; set; }
        public string? RelatedEntityType { get; set; }
        public string? Exchange { get; set; }
        public string? OrderId { get; set; }
        public decimal? ExchangeRate { get; set; }
        public string? Description { get; set; }
        public Dictionary<string, object>? AdditionalData { get; set; }
    }

    /// <summary>
    /// Summary of treasury holdings and revenue
    /// </summary>
    public class TreasurySummaryDto
    {
        public decimal TotalUsdValue { get; set; }
        public decimal TotalPlatformFees { get; set; }
        public decimal TotalDustCollected { get; set; }
        public decimal TotalRounding { get; set; }
        public decimal TotalOther { get; set; }
        public long TotalTransactions { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public List<AssetBalanceSummary> AssetBalances { get; set; } = new();
        public List<DailyRevenue> DailyBreakdown { get; set; } = new();
    }

    /// <summary>
    /// Asset balance summary
    /// </summary>
    public class AssetBalanceSummary
    {
        public string AssetTicker { get; set; } = string.Empty;
        public decimal Balance { get; set; }
        public decimal UsdValue { get; set; }
        public decimal PlatformFeeBalance { get; set; }
        public decimal DustBalance { get; set; }
        public decimal RoundingBalance { get; set; }
        public decimal OtherBalance { get; set; }
    }

    /// <summary>
    /// Daily revenue breakdown
    /// </summary>
    public class DailyRevenue
    {
        public DateTime Date { get; set; }
        public decimal TotalUsd { get; set; }
        public decimal PlatformFees { get; set; }
        public decimal Dust { get; set; }
        public decimal Rounding { get; set; }
        public long TransactionCount { get; set; }
    }

    /// <summary>
    /// Breakdown by source
    /// </summary>
    public class TreasuryBreakdownDto
    {
        public string Source { get; set; } = string.Empty;
        public string TransactionType { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
        public decimal TotalUsdValue { get; set; }
        public long TransactionCount { get; set; }
        public List<AssetAmount> AssetBreakdown { get; set; } = new();
    }

    /// <summary>
    /// Asset amount in breakdown
    /// </summary>
    public class AssetAmount
    {
        public string AssetTicker { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public decimal UsdValue { get; set; }
    }

    /// <summary>
    /// Filter for treasury transaction queries
    /// </summary>
    public class TreasuryTransactionFilter
    {
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string? TransactionType { get; set; }
        public string? Source { get; set; }
        public string? AssetTicker { get; set; }
        public Guid? UserId { get; set; }
        public string? Status { get; set; }
        public string? Exchange { get; set; }
        public bool? IsReported { get; set; }
        public string? ReportingPeriod { get; set; }
        public decimal? MinAmount { get; set; }
        public decimal? MaxAmount { get; set; }
    }

    /// <summary>
    /// Audit trail entry
    /// </summary>
    public class TreasuryAuditEntry
    {
        public Guid TransactionId { get; set; }
        public DateTime Timestamp { get; set; }
        public string Action { get; set; } = string.Empty;
        public string PerformedBy { get; set; } = string.Empty;
        public string? PreviousStatus { get; set; }
        public string? NewStatus { get; set; }
        public string? Notes { get; set; }
        public Dictionary<string, object>? Changes { get; set; }
    }

    /// <summary>
    /// Balance validation result
    /// </summary>
    public class TreasuryValidationResult
    {
        public bool IsValid { get; set; }
        public decimal CalculatedBalance { get; set; }
        public decimal RecordedBalance { get; set; }
        public decimal Difference { get; set; }
        public List<string> Issues { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }

    /// <summary>
    /// DTO for displaying treasury transaction in UI
    /// </summary>
    public class TreasuryTransactionDto
    {
        public Guid Id { get; set; }
        public string TransactionType { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string AssetTicker { get; set; } = string.Empty;
        public decimal? UsdValue { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? CollectedAt { get; set; }
        public string? UserEmail { get; set; }
        public string? Description { get; set; }
        public string? Exchange { get; set; }
        public string? OrderId { get; set; }
    }
}
