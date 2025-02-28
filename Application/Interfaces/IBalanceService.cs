using Domain.DTOs;
using Domain.Models.Balance;
using MongoDB.Bson;

namespace Application.Interfaces
{
    public interface IBalanceService
    {
        public Task<ResultWrapper<IEnumerable<BalanceData>>> GetAllByUserIdAsync(ObjectId userId);
        public Task<ResultWrapper<IEnumerable<BalanceData>>> GetAllBySubscriptionIdAsync(ObjectId subscriptionId);
        public Task<ResultWrapper<BalanceData>> UpsertBalanceAsync(ObjectId transaction_id, ObjectId subscriptionId, BalanceData balance);
    }
}
