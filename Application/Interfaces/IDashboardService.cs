using Domain.DTOs;
using Domain.DTOs.Dashboard;

namespace Application.Interfaces
{
    public interface IDashboardService
    {
        public Task<ResultWrapper<DashboardDto>> GetDashboardDataAsync(Guid userId);
        public Task<ResultWrapper> UpdateDashboardData(
            Guid userId,
            decimal totalInvestments,
            IEnumerable<AssetHoldingsDto> balances,
            decimal portfolioValue
            );
    }
}
