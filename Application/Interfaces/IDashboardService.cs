using Application.Interfaces.Base;
using Domain.DTOs;
using Domain.DTOs.Dashboard;
using Domain.Events;
using Domain.Events.Entity;
using Domain.Models.Dashboard;
using Domain.Models.Subscription;
using MediatR;

namespace Application.Interfaces
{
    public interface IDashboardService :
        IBaseService<DashboardData>,
        INotificationHandler<EntityCreatedEvent<SubscriptionData>>,
        INotificationHandler<PaymentReceivedEvent>
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
