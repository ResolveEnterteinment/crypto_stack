namespace Domain.DTOs.Dashboard
{
    public class DashboardDto
    {
        public IEnumerable<AssetHoldingDto> AssetHoldings { get; set; }
        public decimal TotalInvestments { get; set; }
        public decimal PortfolioValue { get; set; }
    }
}
