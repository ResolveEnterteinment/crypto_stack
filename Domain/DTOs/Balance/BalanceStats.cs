using Domain.Models.Balance;

namespace Domain.DTOs.Balance
{
    /// <summary>
    /// Balance statistics for a user across all assets
    /// </summary>
    public class BalanceStats
    {
        public Guid UserId { get; set; }
        public int TotalBalances { get; set; }
        public int NonZeroBalances { get; set; }
        public DateTime? LastActivityAt { get; set; }
        public long TotalTransactions { get; set; }
        public List<AssetBalance> TopAssets { get; set; } = new();
    }
}
