using Application.Interfaces;
using AspNetCore.Identity.MongoDbCore.Infrastructure;
using Domain.Constants;
using Domain.DTOs;
using Domain.Models.Balance;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Infrastructure.Services
{
    public class BalanceService : BaseService<BalanceData>, IBalanceService
    {
        public BalanceService(
            IOptions<MongoDbSettings> mongoDbSettings,
            IMongoClient mongoClient,
            ILogger<BalanceService> logger) : base(mongoClient, mongoDbSettings, "balances", logger)
        {
        }
        public async Task<ResultWrapper<IEnumerable<BalanceData>>> GetAllBySubscriptionIdAsync(Guid subscriptionId)
        {
            try
            {
                var filter = Builders<BalanceData>.Filter.Eq(b => b.SubscriptionId, subscriptionId);
                var balances = await GetAllAsync(filter);
                if (balances is null)
                {
                    throw new ArgumentNullException(nameof(balances));
                }
                return ResultWrapper<IEnumerable<BalanceData>>.Success(balances);
            }
            catch (Exception ex)
            {
                return ResultWrapper<IEnumerable<BalanceData>>.Failure(FailureReason.From(ex), ex.Message);
            }
        }
        public async Task<ResultWrapper<IEnumerable<BalanceData>>> GetAllByUserIdAsync(Guid userId)
        {
            try
            {
                var filter = Builders<BalanceData>.Filter.Eq(b => b.UserId, userId);
                var balances = await GetAllAsync(filter);
                if (balances is null)
                {
                    throw new ArgumentNullException(nameof(balances));
                }
                return ResultWrapper<IEnumerable<BalanceData>>.Success(balances);
            }
            catch (Exception ex)
            {
                return ResultWrapper<IEnumerable<BalanceData>>.Failure(FailureReason.From(ex), ex.Message);
            }
        }

        public async Task<ResultWrapper<IEnumerable<Guid>>> InitBalances(Guid userId, Guid subscriptionId, IEnumerable<Guid> assets)
        {
            try
            {
                //TO-DO: Add Atomicity
                var insertedBalanceIds = new List<Guid>();
                foreach (var asset in assets)
                {
                    var result = await InsertOneAsync(new BalanceData()
                    {
                        UserId = userId,
                        SubscriptionId = subscriptionId,
                        AssetId = asset
                    });
                    if (!result.IsAcknowledged)
                    {
                        throw new MongoException($"Failed to insert BalanceData");
                    }
                    insertedBalanceIds.Add(result.InsertedId.Value);
                }
                return ResultWrapper<IEnumerable<Guid>>.Success(insertedBalanceIds);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to initialize balances for subscription #{subscriptionId}: {ex.Message}");
                return ResultWrapper<IEnumerable<Guid>>.Failure(FailureReason.DatabaseError, $"Failed to initialize balances for subscription #{subscriptionId}: {ex.Message}");
            }
        }

        public async Task<ResultWrapper<BalanceData>> UpsertBalanceAsync(Guid orderId, Guid subscriptionId, BalanceData updateBalance, IClientSessionHandle session)
        {
            try
            {
                try
                {
                    var filter = Builders<BalanceData>.Filter.Where(b => (b.SubscriptionId == subscriptionId && b.AssetId == updateBalance.AssetId));
                    var balance = await GetOneAsync(filter);
                    var updateAvailable = balance.Available + updateBalance.Available;
                    var updateLocked = balance.Locked + updateBalance.Locked;
                    var updateFields = new
                    {
                        Available = updateAvailable,
                        Locked = updateLocked
                    };
                    await UpdateOneAsync(balance.Id, updateFields, session);
                    return ResultWrapper<BalanceData>.Success(balance);
                }
                catch (Exception)
                {
                    await InsertOneAsync(updateBalance);
                }

                _logger.LogInformation($"Updated subscription #{subscriptionId} balance.");
                return ResultWrapper<BalanceData>.Success(updateBalance);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to update subscription balances: {Message}", ex.Message);
                return ResultWrapper<BalanceData>.Failure(FailureReason.From(ex), ex.Message);
            }
        }
    }
}