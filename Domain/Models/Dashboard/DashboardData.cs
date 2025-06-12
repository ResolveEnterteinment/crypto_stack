using Domain.DTOs.Dashboard;

namespace Domain.Models.Dashboard
{
    public class DashboardData : BaseEntity
    {
        public Guid UserId { get; set; }
        public decimal TotalInvestments { get; set; }
        public IEnumerable<AssetHoldingDto> AssetHoldings { get; set; }
        public decimal PortfolioValue { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}
