namespace Domain.DTOs.Balance
{
    /// <summary>
    /// Cache statistics for monitoring balance cache health
    /// </summary>
    public class BalanceCacheStats
    {
        public Guid UserId { get; set; }
        public bool UserBalanceExists { get; set; }
        public bool BalancesWithAssetsExists { get; set; }
        public int CommonTickerCacheHits { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
