using Application.Interfaces;
using Domain.Constants;
using Domain.DTOs;
using Domain.Events;
using Domain.Models.Balance;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Infrastructure.Services
{
    public class BalanceService : BaseService<BalanceData>, IBalanceService, INotificationHandler<PaymentReceivedEvent>
    {
        private readonly IAssetService _assetService;
        public BalanceService(
            IAssetService assetService,
            IOptions<MongoDbSettings> mongoDbSettings,
            IMongoClient mongoClient,
            ILogger<BalanceService> logger) : base(mongoClient, mongoDbSettings, "balances", logger)
        {
            _assetService = assetService;
        }

        public async Task<ResultWrapper<IEnumerable<BalanceData>>> GetAllByUserIdAsync(Guid userId, string? assetClass = null)
        {
            try
            {
                var filter = Builders<BalanceData>.Filter.Eq(b => b.UserId, userId);
                var balances = await GetAllAsync(filter);

                if (balances is null)
                {
                    throw new ArgumentNullException(nameof(balances));
                }

                var returnBalances = new List<BalanceData>();
                if (assetClass != null && AssetClass.AllValues.Contains(assetClass))
                {
                    foreach (var balance in balances)
                    {
                        var assetData = await _assetService.GetByIdAsync(balance.AssetId);
                        if (assetData.Class.ToLowerInvariant() == assetClass.ToLowerInvariant())
                        {
                            returnBalances.Add(balance);
                        }
                    }
                }

                return ResultWrapper<IEnumerable<BalanceData>>.Success(returnBalances);
            }
            catch (Exception ex)
            {
                return ResultWrapper<IEnumerable<BalanceData>>.FromException(ex);
            }
        }
        public async Task<List<BalanceDto>> FetchBalancesWithAssetsAsync(Guid userId)
        {
            throw new NotImplementedException();
        }

        public async Task<ResultWrapper<BalanceData>> UpsertBalanceAsync(Guid userId, BalanceData updateBalance, IClientSessionHandle? session = null)
        {
            try
            {
                var filter = Builders<BalanceData>.Filter.Eq(s => s.UserId, userId);
                var update = Builders<BalanceData>.Update
                    .Inc(s => s.Available, updateBalance.Available)
                    .Inc(s => s.Locked, updateBalance.Locked)
                    .Set(s => s.LastUpdated, DateTime.UtcNow);

                var updatedBalance = await _collection.FindOneAndUpdateAsync(filter, update, new FindOneAndUpdateOptions<BalanceData>
                {
                    IsUpsert = true // Create if not exists
                });

                _logger.LogInformation($"Updated subscription #{userId} balance.");
                return ResultWrapper<BalanceData>.Success(updatedBalance);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to update subscription balances: {Message}", ex.Message);
                return ResultWrapper<BalanceData>.FromException(ex);
            }
        }

        public async Task Handle(PaymentReceivedEvent notification, CancellationToken cancellationToken)
        {
            try
            {
                var payment = notification.Payment;
                var assetResult = await _assetService.GetByTickerAsync(payment.Currency);
                if (assetResult is null || !assetResult.IsSuccess || assetResult.Data is null)
                {
                    throw new Exception($"Failed to retrieve asset data for ticker {payment.Currency}: {assetResult?.ErrorMessage ?? "Asset result returned null."}");
                }
                await UpsertBalanceAsync(payment.UserId, new()
                {
                    UserId = payment.UserId,
                    AssetId = assetResult.Data.Id,
                    Available = payment.NetAmount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to handle event {notification.GetType().Name}: {ex.Message}");
            }
        }
    }
}