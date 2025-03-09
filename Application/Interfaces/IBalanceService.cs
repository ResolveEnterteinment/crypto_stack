using Domain.DTOs;
using Domain.Interfaces;
using Domain.Models.Balance;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Application.Interfaces
{
    public interface IBalanceService : IRepository<BalanceData>
    {
        public Task<ResultWrapper<IEnumerable<Guid>>> InitBalances(Guid userId, Guid subscriptionId, IEnumerable<Guid> assets);
        public Task<ResultWrapper<IEnumerable<BalanceData>>> GetAllByUserIdAsync(Guid userId);
        public Task<ResultWrapper<IEnumerable<BalanceData>>> GetAllBySubscriptionIdAsync(Guid subscriptionId);
        public Task<ResultWrapper<BalanceData>> UpsertBalanceAsync(Guid transaction_id, Guid subscriptionId, BalanceData balance, IClientSessionHandle session = null);
    }
}
