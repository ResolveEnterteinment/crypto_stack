using Domain.Attributes;
using Domain.Models;

namespace Domain.Models.Treasury
{
    /// <summary>
    /// Aggregate treasury balance by asset
    /// </summary>
    [BsonCollection("treasury_balances")]
    public class TreasuryBalanceData : BaseEntity
    {
        /// <summary>
        /// Asset ticker (BTC, ETH, USDT, USD, etc.)
        /// </summary>
        public required string AssetTicker { get; set; }

        /// <summary>
        /// Asset ID reference
        /// </summary>
        public required Guid AssetId { get; set; }

        /// <summary>
        /// Total balance collected
        /// </summary>
        public decimal TotalBalance { get; set; }

        /// <summary>
        /// Balance from platform fees
        /// </summary>
        public decimal PlatformFeeBalance { get; set; }

        /// <summary>
        /// Balance from dust collection
        /// </summary>
        public decimal DustBalance { get; set; }

        /// <summary>
        /// Balance from rounding differences
        /// </summary>
        public decimal RoundingBalance { get; set; }

        /// <summary>
        /// Balance from other sources (interest, penalties, etc.)
        /// </summary>
        public decimal OtherBalance { get; set; }

        /// <summary>
        /// Total USD equivalent value (cached)
        /// </summary>
        public decimal TotalUsdValue { get; set; }

        /// <summary>
        /// Last exchange rate used for USD conversion
        /// </summary>
        public decimal? LastExchangeRate { get; set; }

        /// <summary>
        /// Last time the USD value was updated
        /// </summary>
        public DateTime? LastUsdUpdateAt { get; set; }

        /// <summary>
        /// Total number of transactions contributing to this balance
        /// </summary>
        public long TransactionCount { get; set; }

        /// <summary>
        /// Last transaction date
        /// </summary>
        public DateTime? LastTransactionAt { get; set; }

        /// <summary>
        /// Exchange where assets are held (if applicable)
        /// </summary>
        public string? Exchange { get; set; }

        /// <summary>
        /// Whether this balance is available for withdrawal
        /// </summary>
        public bool IsAvailableForWithdrawal { get; set; }

        /// <summary>
        /// Locked amount (pending transactions, etc.)
        /// </summary>
        public decimal LockedAmount { get; set; }

        /// <summary>
        /// Available balance (TotalBalance - LockedAmount)
        /// </summary>
        public decimal AvailableBalance => TotalBalance - LockedAmount;
    }
}
