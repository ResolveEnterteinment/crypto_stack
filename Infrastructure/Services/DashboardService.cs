using Application.Interfaces;
using Application.Interfaces.Exchange;
using Domain.DTOs;
using Domain.Models.Crypto;
using Domain.Models.Dashboard;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using MongoDB.Driver.Linq;

namespace Infrastructure.Services
{
    public class DashboardService : BaseService<DashboardData>, IDashboardService
    {
        private readonly IExchangeService _exchangeService;
        private readonly ISubscriptionService _subscriptionService;
        private readonly IAssetService _assetService;
        private readonly IBalanceService _balanceService;
        private readonly IMemoryCache _cache;

        public DashboardService(
            IExchangeService exchangeService,
            ISubscriptionService subscriptionService,
            IAssetService assetService,
            IBalanceService balanceService,
            IMemoryCache cache,
            IOptions<MongoDbSettings> mongoDbSettings,
            IMongoClient mongoClient,
            ILogger<BalanceService> logger
            ) : base(
                mongoClient,
                mongoDbSettings,
                "dashboard",
                logger,
                Builders<DashboardData>.IndexKeys.Ascending(b => b.UserId)
                )
        {
            _exchangeService = exchangeService;
            _subscriptionService = subscriptionService;
            _assetService = assetService;
            _balanceService = balanceService;
            _exchangeService = exchangeService;
            _cache = cache;

            //InitializeIndexes(database).GetAwaiter().GetResult();
        }

        /*private async Task InitializeIndexes(IMongoDatabase database)
        {
            
            await _balances.Indexes.CreateOneAsync(new CreateIndexModel<Balance>(
                Builders<Balance>.IndexKeys.Ascending(b => b.UserId).Ascending(b => b.AssetId)));
            await _assets.Indexes.CreateOneAsync(new CreateIndexModel<Asset>(
                Builders<Asset>.IndexKeys.Ascending(a => a.Ticker)));
            await _subscriptions.Indexes.CreateOneAsync(new CreateIndexModel<Subscription>(
                Builders<Subscription>.IndexKeys.Ascending(s => s.UserId)));
            await _summaries.Indexes.CreateOneAsync(new CreateIndexModel<UserSummary>(
                Builders<UserSummary>.IndexKeys.Ascending(s => s.UserId)));
        }*/

        public async Task<DashboardDto> GetDashboardDataAsync(string userIdString)
        {
            if (!Guid.TryParse(userIdString, out var userId))
            {

            }
            string cacheKey = $"dashboard_{userId}";
            if (_cache.TryGetValue(cacheKey, out DashboardDto cached))
                return cached;

            var balancesTask = FetchBalancesWithAssetsAsync(userId);
            var totalInvestmentsTask = FetchTotalInvestmentsAsync(userId);
            var portfolioValueTask = FetchPortfolioValueAsync(userId);

            await Task.WhenAll(balancesTask, totalInvestmentsTask, portfolioValueTask);

            var result = new DashboardDto
            {
                Balances = balancesTask.Result,
                TotalInvestments = totalInvestmentsTask.Result,
                PortfolioValue = portfolioValueTask.Result.Data
            };

            _cache.Set(cacheKey, result, TimeSpan.FromSeconds(30));
            return result;
        }

        private async Task<List<BalanceDto>> FetchBalancesWithAssetsAsync(Guid userId)
        {
            /*return await _balanceService.Collection.Aggregate()
                .Match(b => b.UserId == userId)
                .Lookup(
                    foreignCollection: _assetService.CollectionName,
                    localField: b => b.AssetId,
                    foreignField: a => a.Id,
                    @as: b => b.AssetDocs)
            .Unwind(b => b.AssetDocs)
                .Project(b => new BalanceDto
                {
                    AssetName = b.AssetDocs.Name,
                    Ticker = b.AssetDocs.Ticker,
                    Available = b.Available,
                    Locked = b.Locked,
                    Total = b.Total
                })
                .ToListAsync();*/
            throw new NotImplementedException();
        }

        private async Task<decimal> FetchTotalInvestmentsAsync(Guid userId)
        {
            var subscriptionResult = await _subscriptionService.GetAllByUserIdAsync(userId);
            return subscriptionResult.Data?.Select(s => s.TotalInvestments).Sum() ?? 0;
        }

        private async Task<ResultWrapper<decimal>> FetchPortfolioValueAsync(Guid userId)
        {
            var summary = await _collection
                .Find(s => s.UserId == userId.ToString())
                .FirstOrDefaultAsync();

            if (summary != null && (DateTime.UtcNow - summary.LastUpdated).TotalMinutes < 5)
                return summary.PortfolioValue;

            var balances = await FetchBalancesWithAssetsAsync(userId);
            decimal portfolioValue = 0;
            foreach (var balance in balances)
            {
                var assetResult = await _assetService.GetByTickerAsync(balance.Ticker);
                if (!ResultWrapper<AssetData>.TryParse(assetResult, out var asset, out var assetErrorMessage, out var assetFailureReason, out var assetValidationErrors))
                {
                    return ResultWrapper<decimal>.Failure(assetFailureReason, assetErrorMessage);
                }

                var rateResult = await _exchangeService.Exchanges[asset.Exchange].GetAssetPrice(balance.Ticker);
                if (!ResultWrapper<decimal>.TryParse(rateResult, out var rate, out var rateErrorMessage, out var rateFailureReason, out var rateValidationErrors))
                {
                    return ResultWrapper<decimal>.Failure(rateFailureReason, rateErrorMessage);
                }
                portfolioValue += balance.Total * rate;
            }

            await UpdatePortfolioValueAsync(userId, portfolioValue);
            return portfolioValue;
        }

        public async Task UpdatePortfolioValueAsync(Guid userId, decimal portfolioValue)
        {
            var filter = Builders<DashboardData>.Filter.Eq("UserId", userId);
            var update = Builders<DashboardData>.Update
                .Set(s => s.PortfolioValue, portfolioValue)
                .Set(s => s.LastUpdated, DateTime.UtcNow);
            await _collection.FindOneAndUpdateAsync(filter, update, new FindOneAndUpdateOptions<DashboardData> { IsUpsert = true });
        }
    }
}