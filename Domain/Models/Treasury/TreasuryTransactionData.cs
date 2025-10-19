using Domain.Attributes;
using Domain.Models;

namespace Domain.Models.Treasury
{
    /// <summary>
    /// Represents a treasury transaction tracking corporate revenue from various sources
    /// </summary>
    [BsonCollection("treasury_transactions")]
    public class TreasuryTransactionData : BaseEntity
    {
        /// <summary>
        /// Type of treasury transaction (Fee, Dust, Rounding, Interest, etc.)
        /// </summary>
        public required string TransactionType { get; set; }

        /// <summary>
        /// Source of the revenue (PlatformFee, StripeFee, DustCollection, RoundingDifference, etc.)
        /// </summary>
        public required string Source { get; set; }

        /// <summary>
        /// Amount collected in the asset's base unit
        /// </summary>
        public required decimal Amount { get; set; }

        /// <summary>
        /// Asset ticker (BTC, ETH, USDT, USD, etc.)
        /// </summary>
        public required string AssetTicker { get; set; }

        /// <summary>
        /// Asset ID reference
        /// </summary>
        public required Guid AssetId { get; set; }

        /// <summary>
        /// User ID if applicable (for user-specific fees/dust)
        /// </summary>
        public Guid? UserId { get; set; }

        /// <summary>
        /// Related transaction ID (payment, order, withdrawal, etc.)
        /// </summary>
        public string? RelatedTransactionId { get; set; }

        /// <summary>
        /// Related entity type (Payment, Order, Withdrawal, etc.)
        /// </summary>
        public string? RelatedEntityType { get; set; }

        /// <summary>
        /// Subscription ID if related to a subscription payment
        /// </summary>
        public Guid? SubscriptionId { get; set; }

        /// <summary>
        /// Exchange name if applicable (Binance, Coinbase, etc.)
        /// </summary>
        public string? Exchange { get; set; }

        /// <summary>
        /// Order ID if related to an exchange order
        /// </summary>
        public string? OrderId { get; set; }

        /// <summary>
        /// Exchange rate at time of transaction (for conversion to USD)
        /// </summary>
        public decimal? ExchangeRate { get; set; }

        /// <summary>
        /// USD equivalent value at time of transaction
        /// </summary>
        public decimal? UsdValue { get; set; }

        /// <summary>
        /// Additional metadata for audit trail
        /// </summary>
        public Dictionary<string, object>? Metadata { get; set; }

        /// <summary>
        /// Description of the transaction
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Status of the transaction (Pending, Collected, Failed, Reversed)
        /// </summary>
        public required string Status { get; set; }

        /// <summary>
        /// Timestamp when the amount was collected
        /// </summary>
        public DateTime? CollectedAt { get; set; }

        /// <summary>
        /// Whether this has been included in financial reports
        /// </summary>
        public bool IsReported { get; set; }

        /// <summary>
        /// Reporting period (e.g., "2025-Q1")
        /// </summary>
        public string? ReportingPeriod { get; set; }

        /// <summary>
        /// Notes for accounting/audit purposes
        /// </summary>
        public string? AuditNotes { get; set; }
    }
}
