using Application.Interfaces;
using Application.Interfaces.Asset;
using Application.Interfaces.Base;
using Application.Interfaces.Logging;
using Domain.Constants;
using Domain.DTOs;
using Domain.DTOs.Balance;
using Domain.Events;
using Domain.Models.Balance;
using Infrastructure.Services.Base;
using MongoDB.Driver;

namespace Infrastructure.Services
{
    public class BalanceService : BaseService<BalanceData>, IBalanceService
    {
        private const string CACHE_KEY_USER_BALANCES = "user_balances:{0}:{1}"; // userId_assetClass
        private static readonly TimeSpan CACHE_DURATION = TimeSpan.FromMinutes(5);

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
        => FetchCached(
                string.Format(CACHE_KEY_USER_BALANCES, userId, assetType ?? "all"),
                async () =>
                {
                    if (userId == Guid.Empty)
                        throw new ArgumentException("Invalid userId");

                    var filter = Builders<BalanceData>.Filter.Eq(b => b.UserId, userId);
                    var allWr = await GetManyAsync(filter);
                    if (!allWr.IsSuccess)
                        throw new Exception(allWr.ErrorMessage);

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
                },
                CACHE_DURATION,
                () => new KeyNotFoundException($"Failed to fetch user {userId} balances")
            );

        public Task<ResultWrapper<List<BalanceDto>>> FetchBalancesWithAssetsAsync(Guid userId)
            => FetchCached(
                $"balances_with_assets:{userId}",
                async () =>
                {
                    var filter = Builders<BalanceData>.Filter.Eq(b => b.UserId, userId);
                    var allWr = await GetManyAsync(filter);
                    if (!allWr.IsSuccess)
                        throw new Exception(allWr.ErrorMessage);

                    var balances = allWr.Data ?? Enumerable.Empty<BalanceData>();
                    var result = new List<BalanceDto>();

                    foreach (var bal in balances)
                    {
                        var assetWr = await _assetService.GetByIdAsync(bal.AssetId);
                        if (!assetWr.IsSuccess || assetWr.Data == null)
                        {
                            await Logger.LogTraceAsync($"Asset not found for ID {bal.AssetId}");
                            continue;
                        }

                        result.Add(new BalanceDto
                        {
                            AssetId = assetWr.Data.Id.ToString(),
                            AssetName = assetWr.Data.Name,
                            Ticker = assetWr.Data.Ticker,
                            Symbol = assetWr.Data.Symbol,
                            Available = bal.Available,
                            Locked = bal.Locked,
                            Total = bal.Total,
                            AssetDocs = assetWr.Data
                        });
                    }

                    return result;
                },
                CACHE_DURATION
            );

        public async Task<ResultWrapper<BalanceData>> UpsertBalanceAsync(Guid userId, BalanceData updateBalance, IClientSessionHandle? session = null)
        {
            if (userId == Guid.Empty)
                return ResultWrapper<BalanceData>.Failure(Domain.Constants.FailureReason.ValidationError, "Invalid userId");
            if (updateBalance == null)
                return ResultWrapper<BalanceData>.Failure(Domain.Constants.FailureReason.ValidationError, "Balance data is null");

            var filter = Builders<BalanceData>.Filter.And(
                Builders<BalanceData>.Filter.Eq(b => b.UserId, userId),
                Builders<BalanceData>.Filter.Eq(b => b.AssetId, updateBalance.AssetId)
            );

            var existingWr = await GetOneAsync(filter);
            BalanceData resultEntity;

            if (existingWr.IsSuccess && existingWr.Data != null)
            {
                var existing = existingWr.Data;
                var fields = new Dictionary<string, object>
                {
                    ["Available"] = existing.Available + updateBalance.Available,
                    ["Locked"] = existing.Locked + updateBalance.Locked,
                    ["Total"] = existing.Total + updateBalance.Available + updateBalance.Locked,
                    ["LastUpdated"] = DateTime.UtcNow
                };
                if (!string.IsNullOrWhiteSpace(updateBalance.Ticker))
                    fields["Ticker"] = updateBalance.Ticker;

                var updateWr = await UpdateAsync(existing.Id, fields);
                if (!updateWr.IsSuccess)
                    return ResultWrapper<BalanceData>.FromException(new Exception(updateWr.ErrorMessage));

                var fetchWr = await GetByIdAsync(existing.Id);
                resultEntity = fetchWr.Data!;
            }
            else
            {
                updateBalance.Id = Guid.NewGuid();
                updateBalance.UserId = userId;
                updateBalance.Total = updateBalance.Available + updateBalance.Locked;
                updateBalance.CreatedAt = DateTime.UtcNow;
                updateBalance.LastUpdated = DateTime.UtcNow;

                var insertWr = await InsertAsync(updateBalance);
                if (!insertWr.IsSuccess)
                    return ResultWrapper<BalanceData>.FromException(new Exception(insertWr.ErrorMessage));

                resultEntity = updateBalance;
            }

            // Invalidate caches
            foreach (var assetClass in AssetType.AllValues)
                CacheService.Invalidate(string.Format(CACHE_KEY_USER_BALANCES, userId, assetClass));
            CacheService.Invalidate(string.Format(CACHE_KEY_USER_BALANCES, userId, "all"));
            CacheService.Invalidate($"balances_with_assets:{userId}");

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

            await UpsertBalanceAsync(p.UserId, new BalanceData
            {
                AssetId = assetWr.Data.Id,
                Ticker = assetWr.Data.Ticker,
                Available = p.NetAmount,
                Locked = 0
            });
        }
    }
}
