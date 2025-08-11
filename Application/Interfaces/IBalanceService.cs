using Application.Interfaces.Base;
using Domain.DTOs;
using Domain.DTOs.Balance;
using Domain.Events.Entity;
using Domain.Models.Balance;
using Domain.Models.Transaction;
using MediatR;
using MongoDB.Driver;

namespace Application.Interfaces
{
    public interface IBalanceService : 
        IBaseService<BalanceData>,
        INotificationHandler<EntityCreatedEvent<TransactionData>>
    {
        Task<ResultWrapper<List<BalanceData>>> GetAllByUserIdAsync(Guid userId, string? assetClass = null);
        Task<ResultWrapper<BalanceData>> GetUserBalanceByTickerAsync(Guid userId, string ticker);
        Task<ResultWrapper<BalanceData>> UpsertBalanceAsync(Guid userId, BalanceUpdateDto balanceUpdateDto, IClientSessionHandle? session = null);
        Task<ResultWrapper<List<BalanceData>>> FetchBalancesWithAssetsAsync(Guid userId, string? assetType = null);
        Task<ResultWrapper> WarmupUserBalanceCacheAsync(Guid userId);
    }
}
