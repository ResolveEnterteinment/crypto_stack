namespace Domain.DTOs.Transaction
{
    /// <summary>
    /// Represents one side of a transaction entry (debit or credit)
    /// </summary>
    public class TransactionEntry
    {
        /// <summary>
        /// User whose balance is affected
        /// </summary>
        public required Guid UserId { get; set; }

        /// <summary>
        /// Asset being transferred
        /// </summary>
        public required Guid AssetId { get; set; }

        /// <summary>
        /// Asset ticker for quick reference (denormalized for performance)
        /// </summary>
        public string? Ticker { get; set; }

        /// <summary>
        /// Amount being transferred
        /// Positive for increases (credits), negative for decreases (debits)
        /// </summary>
        public required decimal Quantity { get; set; }

        /// <summary>
        /// Which part of the balance is affected: Available, Locked, or Both
        /// </summary>
        public BalanceType BalanceType { get; set; } = BalanceType.Available;

        /// <summary>
        /// Balance snapshot before this transaction
        /// </summary>
        public decimal? BalanceBeforeAvailable { get; set; }

        /// <summary>
        /// Balance snapshot before this transaction
        /// </summary>
        public decimal? BalanceBeforeLocked { get; set; }

        /// <summary>
        /// Balance snapshot after this transaction
        /// </summary>
        public decimal? BalanceAfterAvailable { get; set; }

        /// <summary>
        /// Balance snapshot after this transaction
        /// </summary>
        public decimal? BalanceAfterLocked { get; set; }

        /// <summary>
        /// Exchange rate used if this is a conversion (optional)
        /// </summary>
        public decimal? ExchangeRate { get; set; }

        /// <summary>
        /// Reference to the balance document being modified
        /// </summary>
        public Guid? BalanceId { get; set; }

        /// <summary>
        /// Additional metadata specific to this entry
        /// </summary>
        public Dictionary<string, object>? Metadata { get; set; }

        /// <summary>
        /// Validates the transaction entry
        /// </summary>
        public (bool IsValid, string? ErrorMessage) Validate()
        {
            if (UserId == Guid.Empty)
                return (false, "UserId cannot be empty");

            if (AssetId == Guid.Empty)
                return (false, "AssetId cannot be empty");

            if (Quantity == 0)
                return (false, "Quantity cannot be zero");

            // Balance snapshots should be consistent if present
            if (BalanceBeforeAvailable.HasValue && BalanceAfterAvailable.HasValue)
            {
                var expectedAfter = BalanceBeforeAvailable.Value + Quantity;
                var tolerance = 0.00000001m; // Floating point tolerance

                if (Math.Abs(BalanceAfterAvailable.Value - expectedAfter) > tolerance)
                {
                    return (false, $"Balance calculation mismatch: {BalanceBeforeAvailable} + {Quantity} != {BalanceAfterAvailable}");
                }
            }

            return (true, null);
        }
    }
}
