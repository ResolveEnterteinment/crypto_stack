using Application.Interfaces.Base;
using Domain.DTOs;
using Domain.DTOs.Balance;
using Domain.Events;
using Domain.Models.Balance;
using MediatR;
using MongoDB.Driver;

namespace Application.Interfaces
{
    public interface IBalanceService : IBaseService<BalanceData>, INotificationHandler<PaymentReceivedEvent>
    {
        public Task<ResultWrapper<List<BalanceData>>> GetAllByUserIdAsync(Guid userId, string? assetClass = null);
        public Task<ResultWrapper<BalanceData>> UpsertBalanceAsync(Guid userId, BalanceData balance, IClientSessionHandle session = null);
        public Task<ResultWrapper<List<BalanceDto>>> FetchBalancesWithAssetsAsync(Guid userId);
    }
}
