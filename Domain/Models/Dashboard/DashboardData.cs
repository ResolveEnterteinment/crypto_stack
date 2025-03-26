using Domain.DTOs;

namespace Domain.Models.Dashboard
{
    public class DashboardData : BaseEntity
    {
        public string UserId { get; set; }
        public decimal TotalInvestments { get; set; }
        public IEnumerable<BalanceDto> AssetHoldings { get; set; }
        public decimal PortfolioValue { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}
