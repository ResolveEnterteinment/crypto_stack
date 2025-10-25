using Domain.Constants.Transaction;
using Domain.DTOs.Transaction;
using Domain.Models.Transaction;

namespace Domain.DTOs.Balance
{
    /// <summary>
    /// DTO for balance updates - supports both legacy and double-entry transactions
    /// </summary>
    public class BalanceUpdateDto
    {
        public Guid AssetId { get; set; }
        public decimal Available { get; set; }
        public decimal Locked { get; set; }
        public Guid LastTransactionId { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Creates BalanceUpdateDto from a TransactionEntry (double-entry system)
        /// This is the preferred method for new code
        /// </summary>
        public static BalanceUpdateDto CreateFromEntry(TransactionEntry entry, Guid transactionId)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));

            var dto = new BalanceUpdateDto
            {
                AssetId = entry.AssetId,
                Available = 0,
                Locked = 0,
                LastTransactionId = transactionId,
                LastUpdated = DateTime.UtcNow
            };

            // Apply the balance change based on BalanceType
            switch (entry.BalanceType)
            {
                case BalanceType.Available:
                    dto.Available = entry.Quantity;
                    break;

                case BalanceType.Locked:
                    dto.Locked = entry.Quantity;
                    break;

                case BalanceType.LockFromAvailable:
                    // Move from Available to Locked
                    dto.Available = -Math.Abs(entry.Quantity);
                    dto.Locked = Math.Abs(entry.Quantity);
                    break;

                case BalanceType.UnlockToAvailable:
                    // Move from Locked to Available
                    dto.Locked = -Math.Abs(entry.Quantity);
                    dto.Available = Math.Abs(entry.Quantity);
                    break;

                default:
                    throw new InvalidOperationException($"Unsupported BalanceType: {entry.BalanceType}");
            }

            return dto;
        }

        /// <summary>
        /// Creates multiple BalanceUpdateDtos from a double-entry transaction
        /// Returns one DTO per balance entry (FromBalance, ToBalance, Fee, Rounding)
        /// </summary>
        public static List<(Guid UserId, BalanceUpdateDto Dto)> CreateFromDoubleEntry(TransactionData transaction)
        {
            if (transaction == null)
                throw new ArgumentNullException(nameof(transaction));

            var updates = new List<(Guid UserId, BalanceUpdateDto Dto)>();

            // Process FromBalance
            if (transaction.FromBalance != null)
            {
                updates.Add((
                    transaction.FromBalance.UserId,
                    CreateFromEntry(transaction.FromBalance, transaction.Id)
                ));
            }

            // Process ToBalance
            if (transaction.ToBalance != null)
            {
                updates.Add((
                    transaction.ToBalance.UserId,
                    CreateFromEntry(transaction.ToBalance, transaction.Id)
                ));
            }

            // Process Fee
            if (transaction.Fee != null)
            {
                updates.Add((
                    transaction.Fee.UserId,
                    CreateFromEntry(transaction.Fee, transaction.Id)
                ));
            }

            // Process Rounding
            if (transaction.Rounding != null)
            {
                updates.Add((
                    transaction.Rounding.UserId,
                    CreateFromEntry(transaction.Rounding, transaction.Id)
                ));
            }

            return updates;
        }

        /// <summary>
        /// Validates the BalanceUpdateDto
        /// </summary>
        public (bool IsValid, string? ErrorMessage) Validate()
        {
            if (AssetId == Guid.Empty)
                return (false, "AssetId is required");

            if (LastTransactionId == Guid.Empty)
                return (false, "LastTransactionId is required");

            // At least one balance field should be non-zero
            if (Available == 0 && Locked == 0)
                return (false, "At least one of Available or Locked must be non-zero");

            return (true, null);
        }

        /// <summary>
        /// Creates a summary string for logging
        /// </summary>
        public override string ToString()
        {
            return $"Asset: {AssetId}, Available: {Available:F8}, Locked: {Locked:F8}, TxId: {LastTransactionId}";
        }
    }
}