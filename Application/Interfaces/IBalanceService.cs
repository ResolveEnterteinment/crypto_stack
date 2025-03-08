using Domain.DTOs;
using Domain.Interfaces;
using Domain.Models.Balance;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Application.Interfaces
{
    public interface IBalanceService : IRepository<BalanceData>
    {
        public Task<ResultWrapper<IEnumerable<ObjectId>>> InitBalances(ObjectId userId, ObjectId subscriptionId, IEnumerable<ObjectId> assets);
        public Task<ResultWrapper<IEnumerable<BalanceData>>> GetAllByUserIdAsync(ObjectId userId);
        public Task<ResultWrapper<IEnumerable<BalanceData>>> GetAllBySubscriptionIdAsync(ObjectId subscriptionId);
        public Task<ResultWrapper<BalanceData>> UpsertBalanceAsync(ObjectId transaction_id, ObjectId subscriptionId, BalanceData balance, IClientSessionHandle session = null);
    }
}
