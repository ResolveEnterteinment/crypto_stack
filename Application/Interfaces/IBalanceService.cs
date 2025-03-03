using Domain.DTOs;
using Domain.Models.Balance;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Application.Interfaces
{
    public interface IBalanceService
    {
        public Task<bool> InitBalances(ObjectId userId, ObjectId subscriptionId, IEnumerable<ObjectId> assets);
        public Task<ResultWrapper<IEnumerable<BalanceData>>> GetAllByUserIdAsync(ObjectId userId);
        public Task<ResultWrapper<IEnumerable<BalanceData>>> GetAllBySubscriptionIdAsync(ObjectId subscriptionId);
        public Task<ResultWrapper<BalanceData>> UpsertBalanceAsync(ObjectId transaction_id, ObjectId subscriptionId, BalanceData balance);
        public Task<ResultWrapper<BalanceData>> UpsertBalanceAsync(IClientSessionHandle session, ObjectId transaction_id, ObjectId subscriptionId, BalanceData balance);
    }
}
