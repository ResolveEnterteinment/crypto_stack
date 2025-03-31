using Domain.DTOs;

namespace Application.Interfaces
{
    public interface IDashboardService
    {
        public Task<ResultWrapper<DashboardDto>> GetDashboardDataAsync(Guid userId);
        public Task<ResultWrapper> UpdateDashboardData(
            Guid userId,
            decimal totalInvestments,
            IEnumerable<BalanceDto> balances,
            decimal portfolioValue
            );
    }
}
