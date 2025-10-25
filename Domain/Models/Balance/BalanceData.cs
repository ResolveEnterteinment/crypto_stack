using Domain.Attributes;
using Domain.Models.Asset;
using MongoDB.Bson.Serialization.Attributes;

namespace Domain.Models.Balance
{
    /// <summary>
    /// Enhanced balance model with improved tracking and audit capabilities
    /// Represents a user's balance for a specific asset
    /// </summary>
    [BsonCollection("balances")]
    public class BalanceData : BaseEntity
    {
        /// <summary>
        /// User who owns this balance
        /// </summary>
        public Guid UserId { get; set; }

        /// <summary>
        /// Asset this balance represents
        /// </summary>
        public Guid AssetId { get; set; }

        /// <summary>
        /// Asset ticker for quick reference (denormalized)
        /// </summary>
        public string? Ticker { get; set; }

        // ===== Balance Amounts =====

        /// <summary>
        /// Available balance - can be used for transactions immediately
        /// </summary>
        public decimal Available { get; set; } = decimal.Zero;

        /// <summary>
        /// Locked balance - reserved for pending transactions
        /// Examples: pending orders, withdrawal requests, escrow
        /// </summary>
        public decimal Locked { get; set; } = decimal.Zero;

        /// <summary>
        /// Total balance (Available + Locked)
        /// This is the user's complete balance including locked funds
        /// </summary>
        public decimal Total { get; set; } = decimal.Zero;

        // ===== Audit & Tracking =====

        /// <summary>
        /// Last transaction that modified this balance
        /// Useful for audit trail and debugging
        /// </summary>
        public Guid LastTransactionId { get; set; }

        /// <summary>
        /// Timestamp of last transaction
        /// Helps identify stale balances
        /// </summary>
        public DateTime? LastTransactionAt { get; set; }

        /// <summary>
        /// Total number of transactions affecting this balance
        /// Useful for analytics and detecting unusual activity
        /// </summary>
        public long TransactionCount { get; set; } = 0;

        // ===== Denormalized Asset Data =====

        /// <summary>
        /// Denormalized asset data for quick access
        /// Not stored in database (BsonIgnore)
        /// Populated when needed via joins
        /// </summary>
        [BsonIgnore]
        public AssetData? Asset { get; set; }

        // ===== Business Logic =====

        /// <summary>
        /// Validates balance consistency
        /// </summary>
        public (bool IsValid, string? ErrorMessage) Validate()
        {
            // Check for negative values
            if (Available < 0)
                return (false, "Available balance cannot be negative");

            if (Locked < 0)
                return (false, "Locked balance cannot be negative");

            // Verify total calculation
            var calculatedTotal = Available + Locked;
            var tolerance = 0.00000001m; // Floating point tolerance

            if (Math.Abs(Total - calculatedTotal) > tolerance)
            {
                return (false, $"Total mismatch: {Total} != {Available} + {Locked} = {calculatedTotal}");
            }

            // Check for required fields
            if (UserId == Guid.Empty)
                return (false, "UserId is required");

            if (AssetId == Guid.Empty)
                return (false, "AssetId is required");

            return (true, null);
        }

        /// <summary>
        /// Updates the total balance
        /// Call this after modifying Available or Locked
        /// </summary>
        public void RecalculateTotal()
        {
            Total = Available + Locked;
        }

        /// <summary>
        /// Checks if user has sufficient available balance
        /// </summary>
        public bool HasSufficientAvailable(decimal amount)
        {
            return Available >= amount;
        }

        /// <summary>
        /// Checks if user has sufficient locked balance
        /// </summary>
        public bool HasSufficientLocked(decimal amount)
        {
            return Locked >= amount;
        }

        /// <summary>
        /// Checks if user has sufficient total balance (available + locked)
        /// </summary>
        public bool HasSufficientTotal(decimal amount)
        {
            return Total >= amount;
        }

        /// <summary>
        /// Returns a summary string for logging
        /// </summary>
        public string ToSummary()
        {
            return $"User: {UserId}, Asset: {Ticker ?? AssetId.ToString()}, " +
                   $"Available: {Available:F8}, Locked: {Locked:F8}, Total: {Total:F8}";
        }
    }
}