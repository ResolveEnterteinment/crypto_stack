using Application.Interfaces;
using Application.Interfaces.Exchange;
using Domain.DTOs;
using Domain.DTOs.Settings;
using Domain.Exceptions;
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

        private const int PortfolioValueCacheTTLMinutes = 5;

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
                cache,
                new List<CreateIndexModel<DashboardData>>()
                    {
                        new (Builders<DashboardData>.IndexKeys.Ascending(b => b.UserId),
                            new CreateIndexOptions { Name = "UserId_1" })
                    }
                )
        {
            _exchangeService = exchangeService;
            _subscriptionService = subscriptionService;
            _assetService = assetService;
            _balanceService = balanceService;
        }

        public async Task<ResultWrapper<DashboardDto>> GetDashboardDataAsync(Guid userId)
        {
            try
            {
                string cacheKey = $"dashboard_{userId}";

                var cached = await CacheEntityAsync(cacheKey, async () =>
                {
                    var totalInvestments = await FetchTotalInvestmentsAsync(userId);
                    var balancesTask = await FetchBalancesWithAssetsAsync(userId);
                    var portfolioValueTask = await FetchPortfolioValueAsync(userId, balancesTask.Data);

                    await UpdateDashboardData(userId, totalInvestments, balancesTask.Data, portfolioValueTask.Data);

                    var result = new DashboardDto
                    {
                        AssetHoldings = balancesTask.Data,
                        TotalInvestments = totalInvestments,
                        PortfolioValue = portfolioValueTask.Data
                    };
                    return result;
                }, TimeSpan.FromMinutes(1));

                return ResultWrapper<DashboardDto>.Success(cached);
            }
            catch (Exception ex)
            {
                return ResultWrapper<DashboardDto>.FromException(ex);
            }
        }

        private async Task<ResultWrapper<IEnumerable<BalanceDto>>> FetchBalancesWithAssetsAsync(Guid userId)
        {
            try
            {
                // Log the start of the operation
                _logger.LogInformation("Starting to fetch balances with assets for user {UserId}", userId);

                // First get the raw balances to verify data exists
                var rawBalances = await _balanceService.Collection
                    .Find(b => b.UserId == userId)
                    .ToListAsync();

                _logger.LogInformation("Found {Count} raw balances for user {UserId}",
                    rawBalances.Count, userId);

                // If there are no balances, return an empty list
                if (!rawBalances.Any())
                {
                    _logger.LogInformation("No balances found for user {UserId}", userId);
                    return ResultWrapper<IEnumerable<BalanceDto>>.Success(new List<BalanceDto>());
                }

                // Log the asset IDs we're looking up
                _logger.LogInformation("Looking up assets for IDs: {AssetIds}",
                    string.Join(", ", rawBalances.Select(b => b.AssetId)));

                // Use a simpler approach with multiple queries instead of aggregation
                var result = new List<BalanceDto>();

                foreach (var balance in rawBalances)
                {
                    try
                    {
                        // Get the asset for this balance
                        var asset = await _assetService.GetByIdAsync(balance.AssetId);

                        if (asset == null)
                        {
                            _logger.LogWarning("Asset not found for ID {AssetId}, user {UserId}",
                                balance.AssetId, userId);
                            continue;
                        }

                        // Create the DTO
                        var balanceDto = new BalanceDto
                        {
                            AssetName = asset.Name,
                            Ticker = asset.Ticker,
                            Available = balance.Available,
                            Locked = balance.Locked,
                            Total = balance.Total
                        };

                        result.Add(balanceDto);

                        _logger.LogDebug("Successfully mapped balance for asset {Ticker}", asset.Ticker);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing balance for asset {AssetId}", balance.AssetId);
                        // Continue with the next balance instead of failing completely
                    }
                }

                _logger.LogInformation("Returning {Count} balance DTOs for user {UserId}",
                    result.Count, userId);

                return ResultWrapper<IEnumerable<BalanceDto>>.Success(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch balances with assets for user {UserId}: {ErrorMessage}",
                    userId, ex.Message);
                return ResultWrapper<IEnumerable<BalanceDto>>.FromException(ex);
            }
        }

        private async Task<decimal> FetchTotalInvestmentsAsync(Guid userId)
        {
            var subscriptionResult = await _subscriptionService.GetAllByUserIdAsync(userId);
            return subscriptionResult.Data?.Select(s => s.TotalInvestments).Sum() ?? 0;
        }

        private async Task<ResultWrapper<decimal>> FetchPortfolioValueAsync(Guid userId, IEnumerable<BalanceDto> balances)
        {
            try
            {
                var summary = await GetOneAsync(new FilterDefinitionBuilder<DashboardData>().Eq(d => d.UserId, userId));

                if (summary != null && (DateTime.UtcNow - summary.LastUpdated).TotalMinutes < PortfolioValueCacheTTLMinutes)
                    return ResultWrapper<decimal>.Success(summary.PortfolioValue);

                decimal portfolioValue = await CalculatePortfolioValue(balances);
                return ResultWrapper<decimal>.Success(portfolioValue);
            }
            catch (Exception ex)
            {
                return ResultWrapper<decimal>.FromException(ex);
            }

        }

        public async Task<ResultWrapper> UpdateDashboardData(
            Guid userId,
            decimal totalInvestments,
            IEnumerable<BalanceDto> balances,
            decimal portfolioValue
            )
        {
            try
            {
                // First check if the dashboard document exists
                var filter = Builders<DashboardData>.Filter.Eq(s => s.UserId, userId);
                var exists = await _collection.CountDocumentsAsync(filter) > 0;

                if (exists)
                {
                    // Document exists, simple update
                    var update = Builders<DashboardData>.Update
                        .Set(s => s.TotalInvestments, totalInvestments)
                        .Set(s => s.AssetHoldings, balances)
                        .Set(s => s.PortfolioValue, portfolioValue)
                        .Set(s => s.LastUpdated, DateTime.UtcNow);

                    await _collection.UpdateOneAsync(filter, update);
                }
                else
                {
                    // Document doesn't exist, create a new one with explicit Guid ID
                    var dashboard = new DashboardData
                    {
                        Id = Guid.NewGuid(),  // Explicitly set the Guid ID
                        UserId = userId,
                        TotalInvestments = totalInvestments,
                        AssetHoldings = balances,
                        PortfolioValue = portfolioValue,
                        LastUpdated = DateTime.UtcNow
                    };

                    await _collection.InsertOneAsync(dashboard);
                }

                return ResultWrapper.Success();
            }
            catch (Exception ex)
            {
                return ResultWrapper.FromException(ex);
            }
        }

        private async Task<decimal> CalculatePortfolioValue(IEnumerable<BalanceDto> balances)
        {
            var portfolioValue = 0m;

            foreach (var balance in balances)
            {
                var assetResult = await _assetService.GetByTickerAsync(balance.Ticker);
                if (!ResultWrapper<AssetData>.TryParse(assetResult, out var asset))
                {
                    throw new AssetFetchException($"Failed to fetch asset for ticker {balance.Ticker}: {assetResult.ErrorMessage}");
                }
                if (asset.Exchange == "Platform")
                    continue;

                var rateResult = await _exchangeService.Exchanges[asset.Exchange].GetAssetPrice(balance.Ticker);
                if (!ResultWrapper<decimal>.TryParse(rateResult, out var rate))
                {
                    throw new ExchangeApiException(rateResult.ErrorMessage, this.GetType().Name);
                }

                portfolioValue += balance.Total * rate;
            }
            return portfolioValue;
        }

    }
}