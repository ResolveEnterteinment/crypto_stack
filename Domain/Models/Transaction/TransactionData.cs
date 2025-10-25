using Domain.Attributes;
using Domain.Constants.Transaction;
using Domain.DTOs.Transaction;

namespace Domain.Models.Transaction
{
    /// <summary>
    /// Transaction model implementing double-entry accounting
    /// Every transaction records both debit and credit sides simultaneously
    /// </summary>
    [BsonCollection("transactions")]
    public class TransactionData : BaseEntity
    {
        // ===== Core Transaction Info =====

        /// <summary>
        /// Primary user involved in the transaction (typically the "from" user)
        /// </summary>
        public required Guid UserId { get; set; }

        /// <summary>
        /// External payment provider identifier (Stripe, PayPal, etc.)
        /// </summary>
        public string? PaymentProviderId { get; set; }

        /// <summary>
        /// Associated subscription if this is a recurring payment
        /// </summary>
        public Guid? SubscriptionId { get; set; }

        /// <summary>
        /// The source system/service that initiated this transaction
        /// Examples: "Stripe", "Binance", "Internal", "System"
        /// </summary>
        public required string SourceName { get; set; } = string.Empty;

        /// <summary>
        /// Unique identifier in the source system
        /// </summary>
        public required string SourceId { get; set; } = string.Empty;

        /// <summary>
        /// Transaction type: Deposit, Withdrawal, Trade, Fee, Transfer, etc.
        /// </summary>
        public required string Action { get; set; }

        /// <summary>
        /// Human-readable description of the transaction
        /// </summary>
        public string? Description { get; set; } = string.Empty;

        // ===== Double-Entry Accounting Fields =====

        /// <summary>
        /// Source balance entry (debit side) - what's being reduced or spent
        /// Null for external deposits (money coming into the system)
        /// </summary>
        public TransactionEntry? FromBalance { get; set; }

        /// <summary>
        /// Destination balance entry (credit side) - what's being increased or received
        /// Null for external withdrawals (money leaving the system)
        /// </summary>
        public TransactionEntry? ToBalance { get; set; }

        /// <summary>
        /// Fee charged for this transaction (if any)
        /// Examples: Platform fee, network fee, exchange fee
        /// </summary>
        public TransactionEntry? Fee { get; set; }

        /// <summary>
        /// Rounding adjustment to handle decimal precision issues
        /// Used when conversions result in amounts that need rounding
        /// </summary>
        public TransactionEntry? Rounding { get; set; }

        // ===== Validation & Audit =====

        /// <summary>
        /// Indicates if this transaction has been validated and confirmed
        /// </summary>
        public bool IsConfirmed { get; set; } = false;

        /// <summary>
        /// Timestamp when transaction was confirmed
        /// </summary>
        public DateTime? ConfirmedAt { get; set; }

        /// <summary>
        /// For reversal/refund transactions, references the original transaction
        /// </summary>
        public Guid? ReversalOfTransactionId { get; set; }

        /// <summary>
        /// Indicates if this transaction has been reversed/refunded
        /// </summary>
        public bool IsReversed { get; set; } = false;

        /// <summary>
        /// Transaction that reversed this one (if applicable)
        /// </summary>
        public Guid? ReversedByTransactionId { get; set; }

        // ===== Business Logic Methods =====

        /// <summary>
        /// Validates that the transaction follows double-entry accounting rules
        /// </summary>
        public (bool IsValid, Dictionary<string, string[]>? ValidationErrors) Validate()
        {
            var validationErrors = new Dictionary<string, string[]>();

            // At least one side must be present
            if (FromBalance == null && ToBalance == null)
            {
                validationErrors.TryAdd("Invalid transaction", ["Transaction must have at least a FromBalance or ToBalance"]);
                return (false, validationErrors);
            }

            // External deposits should have ToBalance only
            if (Action == TransactionActionType.Deposit && FromBalance != null)
            {
                // This might be valid for internal transfers
                // Only invalid if it's truly an external deposit
                validationErrors.TryAdd("FromBalance", ["External deposits should have ToBalance only"]);
            }

            // External withdrawals should have FromBalance only
            if (Action == TransactionActionType.Withdrawal && ToBalance != null)
            {
                // This might be valid for internal transfers
                // Only invalid if it's truly an external withdrawal
                validationErrors.TryAdd("ToBalance", ["External withdrawals should have FromBalance only"]);
            }

            // Validate entries
            if (FromBalance != null)
            {
                var (isValid, error) = FromBalance.Validate();
                if (!isValid) validationErrors.TryAdd("FromBalance", [error]);
            }

            if (ToBalance != null)
            {
                var (isValid, error) = ToBalance.Validate();
                if (!isValid) validationErrors.TryAdd("ToBalance", [error]);
            }

            if (Fee != null)
            {
                var (isValid, error) = Fee.Validate();
                if (!isValid) validationErrors.TryAdd("Fee", [error]);
            }

            if (Rounding != null)
            {
                var (isValid, error) = Rounding.Validate();
                if (!isValid) validationErrors.TryAdd("Rounding", [error]);
            }

            if(validationErrors.Count > 0)
                return (false, validationErrors);

            return (true, null);
        }

        /// <summary>
        /// Gets all unique user IDs involved in this transaction
        /// Useful for cache invalidation and notifications
        /// </summary>
        public IEnumerable<Guid> GetAffectedUserIds()
        {
            var userIds = new HashSet<Guid> { UserId };

            if (FromBalance?.UserId != null && FromBalance.UserId != Guid.Empty)
                userIds.Add(FromBalance.UserId);

            if (ToBalance?.UserId != null && ToBalance.UserId != Guid.Empty)
                userIds.Add(ToBalance.UserId);

            if (Fee?.UserId != null && Fee.UserId != Guid.Empty)
                userIds.Add(Fee.UserId);

            return userIds;
        }

        /// <summary>
        /// Gets all unique asset IDs involved in this transaction
        /// </summary>
        public IEnumerable<Guid> GetAffectedAssetIds()
        {
            var assetIds = new HashSet<Guid>();

            if (FromBalance?.AssetId != null && FromBalance.AssetId != Guid.Empty)
                assetIds.Add(FromBalance.AssetId);

            if (ToBalance?.AssetId != null && ToBalance.AssetId != Guid.Empty)
                assetIds.Add(ToBalance.AssetId);

            if (Fee?.AssetId != null && Fee.AssetId != Guid.Empty)
                assetIds.Add(Fee.AssetId);

            if (Rounding?.AssetId != null && Rounding.AssetId != Guid.Empty)
                assetIds.Add(Rounding.AssetId);

            return assetIds;
        }
    }
}