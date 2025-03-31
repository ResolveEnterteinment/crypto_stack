namespace Domain.DTOs
{
    public class DashboardDto
    {
        public IEnumerable<BalanceDto> AssetHoldings { get; set; }
        public decimal TotalInvestments { get; set; }
        public decimal PortfolioValue { get; set; }
    }
}
