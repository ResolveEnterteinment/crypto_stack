using Application.Interfaces;
using Application.Interfaces.Asset;
using Application.Interfaces.Base;
using Application.Interfaces.Logging;
using Domain.Constants.Asset;
using Domain.DTOs;
using Domain.Events;
using Domain.Exceptions;
using Domain.Models.Balance;
using Infrastructure.Services.Base;
using MongoDB.Driver;

namespace Infrastructure.Services
{
    public class BalanceService : BaseService<BalanceData>, IBalanceService
    {
        private readonly IAssetService _assetService;

        public BalanceService(
            ICrudRepository<BalanceData> repository,
            ICacheService<BalanceData> cacheService,
            IMongoIndexService<BalanceData> indexService,
            ILoggingService logger,
            IEventService eventService,
            IAssetService assetService
        ) : base(
            repository,
            cacheService,
            indexService,
            logger,
            eventService,
            new[]
            {
                new CreateIndexModel<BalanceData>(Builders<BalanceData>.IndexKeys.Ascending(b => b.UserId), new CreateIndexOptions { Name = "UserId_1" }),
                new CreateIndexModel<BalanceData>(Builders<BalanceData>.IndexKeys.Ascending(b => b.AssetId), new CreateIndexOptions { Name = "AssetId_1" })
            }
        )
        {
            _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
        }

        public Task<ResultWrapper<List<BalanceData>>> GetAllByUserIdAsync(Guid userId, string? assetType = null)
        {
            return SafeExecute(
                async () =>
                {
                    if (userId == Guid.Empty)
                    {
                        throw new ArgumentException("Invalid userId");
                    }

                    var filter = Builders<BalanceData>.Filter.Eq(b => b.UserId, userId);
                    var allWr = await GetManyAsync(filter);
                    if (!allWr.IsSuccess)
                    {
                        throw new Exception(allWr.ErrorMessage);
                    }

                    var balances = allWr.Data ?? Enumerable.Empty<BalanceData>();
                    var filtered = new List<BalanceData>();

                    if (!string.IsNullOrWhiteSpace(assetType) && AssetType.AllValues.Contains(assetType))
                    {
                        foreach (var bal in balances)
                        {
                            var assetWr = await _assetService.GetByIdAsync(bal.AssetId);
                            if (assetWr.IsSuccess && assetWr.Data != null &&
                                assetWr.Data.Type.Equals(assetType, StringComparison.OrdinalIgnoreCase))
                            {
                                filtered.Add(bal);
                            }
                        }
                    }
                    else
                    {
                        filtered = balances.ToList();
                    }

                    return filtered;
                }
            );
        }

        public Task<ResultWrapper<BalanceData>> GetUserBalanceByTickerAsync(Guid userId, string ticker)
        {
            return SafeExecute(
                        async () =>
                        {
                            if (userId == Guid.Empty)
                            {
                                throw new ArgumentException("Invalid userId");
                            }

                            var filter = Builders<BalanceData>.Filter.Where(b => b.UserId == userId && b.Ticker == ticker);
                            var balance = await _repository.GetOneAsync(filter);
                            return balance ?? throw new DatabaseException("Failed to fetch user balance for " + ticker);
                        }
                    );
        }
        public Task<ResultWrapper<List<BalanceData>>> FetchBalancesWithAssetsAsync(Guid userId, string? assetType = null)
        {
            return SafeExecute(
                async () =>
                {
                    var filter = Builders<BalanceData>.Filter.And([
                        Builders<BalanceData>.Filter.Eq(b => b.UserId, userId),
                        Builders<BalanceData>.Filter.Gt<decimal>(b => b.Total, 0m),
                        ]);
                    var balances = await _repository.GetAllAsync(filter);
                    if (balances is null)
                    {
                        throw new DatabaseException("Failed to fetch user balances with assets");
                    }

                    var result = new List<BalanceData>();

                    foreach (var bal in balances)
                    {
                        var assetWr = await _assetService.Repository.GetByIdAsync(bal.AssetId);
                        if (assetWr == null)
                        {
                            await Logger.LogTraceAsync($"Asset not found for ID {bal.AssetId}");
                            continue;
                        }

                        result.Add(new BalanceData
                        {
                            Id = bal.Id,
                            Available = bal.Available,
                            Locked = bal.Locked,
                            Total = bal.Total,
                            Asset = assetWr,
                            LastUpdated = bal.LastUpdated
                        });
                    }

                    return string.IsNullOrWhiteSpace(assetType) ? result : result.FindAll(b => string.Equals(b.Asset!.Type, assetType, StringComparison.OrdinalIgnoreCase));
                }
            );
        }

        public async Task<ResultWrapper<BalanceData>> UpsertBalanceAsync(Guid userId, BalanceData updateBalance, IClientSessionHandle? session = null)
        {
            if (userId == Guid.Empty)
            {
                return ResultWrapper<BalanceData>.Failure(Domain.Constants.FailureReason.ValidationError, "Invalid userId");
            }

            if (updateBalance == null)
            {
                return ResultWrapper<BalanceData>.Failure(Domain.Constants.FailureReason.ValidationError, "Balance data is null");
            }

            var filter = Builders<BalanceData>.Filter.And(
                Builders<BalanceData>.Filter.Eq(b => b.UserId, userId),
                Builders<BalanceData>.Filter.Eq(b => b.AssetId, updateBalance.AssetId)
            );

            var existing = await _repository.GetOneAsync(filter);
            BalanceData resultEntity;

            if (existing != null)
            {
                var fields = new Dictionary<string, object>
                {
                    ["Available"] = existing.Available + updateBalance.Available,
                    ["Locked"] = existing.Locked + updateBalance.Locked,
                    ["Total"] = existing.Total + updateBalance.Available + updateBalance.Locked,
                    ["LastUpdated"] = DateTime.UtcNow
                };
                if (!string.IsNullOrWhiteSpace(updateBalance.Ticker))
                {
                    fields["Ticker"] = updateBalance.Ticker;
                }

                var updateWr = await UpdateAsync(existing.Id, fields);
                if (!updateWr.IsSuccess)
                {
                    return ResultWrapper<BalanceData>.FromException(new Exception(updateWr.ErrorMessage));
                }

                var fetchWr = await GetByIdAsync(existing.Id);
                resultEntity = fetchWr.Data!;
            }
            else
            {
                updateBalance.UserId = userId;
                updateBalance.Total = updateBalance.Available + updateBalance.Locked;
                updateBalance.LastUpdated = DateTime.UtcNow;

                var insertWr = await InsertAsync(updateBalance);
                if (!insertWr.IsSuccess)
                {
                    return ResultWrapper<BalanceData>.FromException(new Exception(insertWr.ErrorMessage));
                }

                resultEntity = updateBalance;
            }

            return ResultWrapper<BalanceData>.Success(resultEntity);
        }

        public async Task Handle(PaymentReceivedEvent notification, CancellationToken cancellationToken)
        {
            var p = notification.Payment;
            // Fetch asset by currency ticker
            var assetWr = await _assetService.GetByTickerAsync(p.Currency);
            if (assetWr == null || !assetWr.IsSuccess || assetWr.Data == null)
            {
                await Logger.LogTraceAsync($"Asset not found for currency {p.Currency}");
                return;
            }

            _ = await UpsertBalanceAsync(p.UserId, new BalanceData
            {
                AssetId = assetWr.Data.Id,
                Ticker = assetWr.Data.Ticker,
                Available = p.NetAmount,
                Locked = 0
            });
        }
    }
}
