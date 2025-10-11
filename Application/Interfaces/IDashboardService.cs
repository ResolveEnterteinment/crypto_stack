using Application.Interfaces.Base;
using Domain.DTOs;
using Domain.DTOs.Dashboard;
using Domain.Events;
using Domain.Events.Entity;
using Domain.Models.Balance;
using Domain.Models.Dashboard;
using Domain.Models.Exchange;
using Domain.Models.Withdrawal;
using MediatR;

namespace Application.Interfaces
{
    public interface IDashboardService :
        IBaseService<DashboardData>,
        INotificationHandler<EntityCreatedEvent<BalanceData>>,
        INotificationHandler<EntityUpdatedEvent<BalanceData>>,
        INotificationHandler<EntityCreatedEvent<ExchangeOrderData>>,
        INotificationHandler<EntityCreatedEvent<WithdrawalData>>
    {
        public Task<ResultWrapper<DashboardDto>> GetDashboardDataAsync(Guid userId);
        public Task<ResultWrapper> UpdateDashboardData(
            Guid userId,
            decimal totalInvestments,
            IEnumerable<AssetHoldingDto> balances,
            decimal portfolioValue
            );
        public void InvalidateDashboardCacheAsync(Guid userId);
        public Task InvalidateCacheAndPush(Guid userId);
        Task<ResultWrapper> WarmupUserCacheAsync(Guid userId);
    }
}
