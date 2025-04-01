using Domain.DTOs;
using Domain.DTOs.Balance;
using Domain.Models.Balance;
using MongoDB.Driver;

namespace Application.Interfaces
{
    public interface IBalanceService : IRepository<BalanceData>
    {
        public Task<ResultWrapper<IEnumerable<BalanceData>>> GetAllByUserIdAsync(Guid userId, string? assetClass = null);
        public Task<ResultWrapper<BalanceData>> UpsertBalanceAsync(Guid subscriptionId, BalanceData balance, IClientSessionHandle session = null);
        public Task<List<BalanceDto>> FetchBalancesWithAssetsAsync(Guid userId);
    }
}
