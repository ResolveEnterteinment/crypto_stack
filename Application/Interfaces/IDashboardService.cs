using Application.Interfaces.Base;
using Domain.DTOs;
using Domain.DTOs.Dashboard;
using Domain.Models.Dashboard;

namespace Application.Interfaces
{
    public interface IDashboardService : IBaseService<DashboardData>
    {
        public Task<ResultWrapper<DashboardDto>> GetDashboardDataAsync(Guid userId);
        public Task<ResultWrapper> UpdateDashboardData(
            Guid userId,
            decimal totalInvestments,
            IEnumerable<AssetHoldingsDto> balances,
            decimal portfolioValue
            );
        public Task InvalidateDashboardCacheAsync(Guid userId);
    }
}
