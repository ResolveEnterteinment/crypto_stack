using Application.Interfaces.Base;
using Domain.DTOs;
using Domain.Events;
using Domain.Models.Balance;
using MediatR;
using MongoDB.Driver;

namespace Application.Interfaces
{
    public interface IBalanceService : IBaseService<BalanceData>, INotificationHandler<PaymentReceivedEvent>
    {
        Task<ResultWrapper<List<BalanceData>>> GetAllByUserIdAsync(Guid userId, string? assetClass = null);
        Task<ResultWrapper<BalanceData>> GetUserBalanceByTickerAsync(Guid userId, string ticker);
        Task<ResultWrapper<BalanceData>> UpsertBalanceAsync(Guid userId, BalanceData balance, IClientSessionHandle? session = null);
        Task<ResultWrapper<List<BalanceData>>> FetchBalancesWithAssetsAsync(Guid userId, string? assetType = null);
    }
}
