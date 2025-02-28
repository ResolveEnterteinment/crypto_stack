using Application.Interfaces;
using AspNetCore.Identity.MongoDbCore.Infrastructure;
using Domain.Constants;
using Domain.DTOs;
using Domain.Models.Balance;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
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

        public async Task<ResultWrapper<IEnumerable<BalanceData>>> GetAllBySubscriptionIdAsync(ObjectId subscriptionId)
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
                string reason = ex switch
                {
                    ArgumentNullException => FailureReason.DatabaseError,
                    ArgumentException => FailureReason.ValidationError,
                    KeyNotFoundException => FailureReason.DataNotFound,
                    _ => FailureReason.Unknown
                };
                return ResultWrapper<IEnumerable<BalanceData>>.Failure(reason, ex.Message);
            }
        }
        public async Task<ResultWrapper<IEnumerable<BalanceData>>> GetAllByUserIdAsync(ObjectId userId)
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
                string reason = ex switch
                {
                    ArgumentNullException => FailureReason.DatabaseError,
                    ArgumentException => FailureReason.ValidationError,
                    KeyNotFoundException => FailureReason.DataNotFound,
                    _ => FailureReason.Unknown
                };
                return ResultWrapper<IEnumerable<BalanceData>>.Failure(reason, ex.Message);
            }
        }
        public async Task<ResultWrapper<BalanceData>> UpsertBalanceAsync(ObjectId orderId, ObjectId subscriptionId, BalanceData updateBalance)
        {
            try
            {
                try
                {
                    var filter = Builders<BalanceData>.Filter.Where(b => (b.SubscriptionId == subscriptionId && b.CoinId == updateBalance.CoinId));
                    var balance = await GetOneAsync(filter);
                    var updateQuantity = balance.Quantity + updateBalance.Quantity;
                    var updateOrders = balance.Orders.Append(orderId).ToList();
                    var updateFields = new
                    {
                        Quantity = updateQuantity,
                        Orders = updateOrders,
                    };
                    await UpdateAsync(balance._id, updateFields);
                    return ResultWrapper<BalanceData>.Success(balance);
                }
                catch (Exception)
                {
                    updateBalance.Orders.Append(orderId);
                    await InsertOneAsync(updateBalance);
                }

                _logger.LogInformation($"Updated subscription #{subscriptionId} balance.");
                return ResultWrapper<BalanceData>.Success(updateBalance);
            }
            catch (Exception ex)
            {
                string reason = ex switch
                {
                    ArgumentException => FailureReason.ValidationError,
                    KeyNotFoundException => FailureReason.DataNotFound,
                    MongoException => FailureReason.DatabaseError,
                    _ when ex.Message.Contains("insert") => FailureReason.DatabaseError,
                    _ when ex.Message.Contains("update") => FailureReason.DatabaseError,
                    _ => FailureReason.Unknown
                };
                _logger.LogError(ex, "Unable to update subscription balances: {Message}", ex.Message);
                return ResultWrapper<BalanceData>.Failure(reason, ex.Message);
            }
        }
    }
}