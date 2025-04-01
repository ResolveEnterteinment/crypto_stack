using Application.Interfaces;
using Application.Interfaces.Exchange;
using Domain.DTOs;
using Domain.DTOs.Dashboard;
using Domain.DTOs.Settings;
using Domain.Exceptions;
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
                    var totalInvestmentsTask = await FetchTotalInvestmentsAsync(userId);
                    var balancesTask = await FetchAssetHoldingsAsync(userId);
                    var portfolioValueTask = await FetchPortfolioValueAsync(userId, balancesTask.Data);

                    await UpdateDashboardData(userId, totalInvestmentsTask.Data, balancesTask.Data, portfolioValueTask.Data);

                    var result = new DashboardDto
                    {
                        AssetHoldings = balancesTask.Data,
                        TotalInvestments = totalInvestmentsTask.Data,
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

        private async Task<ResultWrapper<IEnumerable<AssetHoldingsDto>>> FetchAssetHoldingsAsync(Guid userId)
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
                    return ResultWrapper<IEnumerable<AssetHoldingsDto>>.Success(new List<AssetHoldingsDto>());
                }

                // Log the asset IDs we're looking up
                _logger.LogInformation("Looking up assets for IDs: {AssetIds}",
                    string.Join(", ", rawBalances.Select(b => b.AssetId)));

                // Use a simpler approach with multiple queries instead of aggregation
                var result = new List<AssetHoldingsDto>();

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

                        var rateResult = await _exchangeService.Exchanges[asset.Exchange].GetAssetPrice(asset.Ticker);
                        if (rateResult == null || !rateResult.IsSuccess)
                        {
                            _logger.LogWarning($"Failed to fetch {asset.Ticker} value from exchange {asset.Exchange}: {rateResult?.ErrorMessage ?? "Asset price fetch returned null."}");

                        }
                        var value = balance.Total * rateResult?.Data ?? 0m;
                        // Create the DTO
                        var asetHoldingsDto = new AssetHoldingsDto
                        {
                            Name = asset.Name,
                            Ticker = asset.Ticker,
                            Available = balance.Available,
                            Locked = balance.Locked,
                            Total = balance.Total,
                            Value = value
                        };

                        result.Add(asetHoldingsDto);

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

                return ResultWrapper<IEnumerable<AssetHoldingsDto>>.Success(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch balances with assets for user {UserId}: {ErrorMessage}",
                    userId, ex.Message);
                return ResultWrapper<IEnumerable<AssetHoldingsDto>>.FromException(ex);
            }
        }

        private async Task<ResultWrapper<decimal>> FetchTotalInvestmentsAsync(Guid userId)
        {
            try
            {
                var subscriptionResult = await _subscriptionService.GetAllByUserIdAsync(userId);

                if (subscriptionResult == null || !subscriptionResult.IsSuccess || subscriptionResult.Data == null)
                {
                    throw new SubscriptionFetchException($"Failed to fetch subscriptions for user {userId}: {subscriptionResult?.ErrorMessage ?? "Subscription fetch returned null."}");
                }

                decimal total = subscriptionResult.Data?.Select(s => s.TotalInvestments).Sum() ?? 0m;

                return ResultWrapper<decimal>.Success(total);
            }
            catch (Exception ex)
            {
                return ResultWrapper<decimal>.FromException(ex);
            }

        }

        private async Task<ResultWrapper<decimal>> FetchPortfolioValueAsync(Guid userId, IEnumerable<AssetHoldingsDto> assetHoldings)
        {
            try
            {
                var summary = await GetOneAsync(new FilterDefinitionBuilder<DashboardData>().Eq(d => d.UserId, userId));

                if (summary != null && (DateTime.UtcNow - summary.LastUpdated).TotalMinutes < PortfolioValueCacheTTLMinutes)
                    return ResultWrapper<decimal>.Success(summary.PortfolioValue);

                decimal portfolioValue = CalculatePortfolioValue(assetHoldings);
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
            IEnumerable<AssetHoldingsDto> assetHoldings,
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
                        .Set(s => s.AssetHoldings, assetHoldings)
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
                        AssetHoldings = assetHoldings,
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

        private decimal CalculatePortfolioValue(IEnumerable<AssetHoldingsDto> assetHoldings)
        {
            var portfolioValue = 0m;

            foreach (var asset in assetHoldings)
            {
                portfolioValue += asset.Value;
            }
            return portfolioValue;
        }

    }
}