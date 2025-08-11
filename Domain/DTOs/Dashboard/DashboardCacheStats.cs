namespace Domain.DTOs.Dashboard
{
    /// <summary>
    /// Cache statistics for monitoring dashboard cache health
    /// </summary>
    public class DashboardCacheStats
    {
        public Guid UserId { get; set; }
        public bool DashboardDtoExists { get; set; }
        public bool TotalInvestmentsExists { get; set; }
        public bool AssetHoldingsExists { get; set; }
        public bool EntityExists { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
