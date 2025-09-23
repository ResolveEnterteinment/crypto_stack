using Domain.Constants.Transaction;
using Domain.Models.Transaction;

namespace Domain.DTOs.Balance
{
    public class BalanceUpdateDto
    {
        public Guid AssetId { get; set; }
        public decimal Available { get; set; }
        public decimal Locked { get; set; }
        public Guid LastTransactionId { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        public static BalanceUpdateDto FromTransaction(TransactionData transaction)
        {
            return transaction.Action switch
            {
                TransactionActionType.Buy => new BalanceUpdateDto
                {
                    AssetId = transaction.AssetId,
                    Available = transaction.Quantity,
                    LastTransactionId = transaction.Id,
                },
                TransactionActionType.Sell => new BalanceUpdateDto
                {
                    AssetId = transaction.AssetId,
                    Available = transaction.Quantity,
                    LastTransactionId = transaction.Id,
                },
                TransactionActionType.Deposit => new BalanceUpdateDto
                {
                    AssetId = transaction.AssetId,
                    Available = transaction.Quantity,
                    LastTransactionId = transaction.Id,
                },
                TransactionActionType.Withdrawal => new BalanceUpdateDto
                {
                    AssetId = transaction.AssetId,
                    Available = transaction.Quantity,
                    LastTransactionId = transaction.Id,
                },
                TransactionActionType.Lock => new BalanceUpdateDto
                {
                    AssetId = transaction.AssetId,
                    Available = -transaction.Quantity,
                    Locked = transaction.Quantity,
                    LastTransactionId = transaction.Id,
                },
                TransactionActionType.Unlock => new BalanceUpdateDto
                {
                    AssetId = transaction.AssetId,
                    Available = transaction.Quantity,
                    Locked = -transaction.Quantity,
                    LastTransactionId = transaction.Id,
                },
                _ => throw new InvalidOperationException($"Unsupported transaction action: {transaction.Action}")

            };
        }
    }
}
