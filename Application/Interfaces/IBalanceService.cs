using Domain.DTOs;
using Domain.Interfaces;
using Domain.Models.Balance;
using MongoDB.Driver;

namespace Application.Interfaces
{
    public interface IBalanceService : IRepository<BalanceData>
    {
        public Task<ResultWrapper<IEnumerable<BalanceData>>> GetAllByUserIdAsync(Guid userId, string? assetClass = null);
        public Task<ResultWrapper<BalanceData>> UpsertBalanceAsync(Guid subscriptionId, BalanceData balance, IClientSessionHandle session = null);
    }
}
