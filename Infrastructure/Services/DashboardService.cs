using Application.Interfaces;
using Application.Interfaces.Exchange;
using Domain.Constants;
using Domain.DTOs;
using Domain.DTOs.Dashboard;
using Domain.DTOs.Settings;
using Domain.Exceptions;
using Domain.Models.Dashboard;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Infrastructure.Services
{
    public class DashboardService : BaseService<DashboardData>, IDashboardService
    {
        private readonly IExchangeService _exchangeService;
        private readonly ISubscriptionService _subscriptionService;
        private readonly IAssetService _assetService;
        private readonly IBalanceService _balanceService;

        private const string CACHE_KEY_DASHBOARD = "dashboard:{0}";
        private const int DASHBOARD_CACHE_MINUTES = 2;

        public DashboardService(
            IExchangeService exchangeService,
            ISubscriptionService subscriptionService,
            IAssetService assetService,
            IBalanceService balanceService,
            IMemoryCache cache,
            IOptions<MongoDbSettings> mongoDbSettings,
            IMongoClient mongoClient,
            ILogger<DashboardService> logger
            ) : base(
                mongoClient,
                mongoDbSettings,
                "dashboard",
                logger,
                cache,
                new List<CreateIndexModel<DashboardData>>()
                    {
                        new (Builders<DashboardData>.IndexKeys.Ascending(b => b.UserId),
                            new CreateIndexOptions { Name = "UserId_1", Unique = true })
                    }
                )
        {
            _exchangeService = exchangeService ?? throw new ArgumentNullException(nameof(exchangeService));
            _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
            _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
            _balanceService = balanceService ?? throw new ArgumentNullException(nameof(balanceService));
        }

        public async Task<ResultWrapper<DashboardDto>> GetDashboardDataAsync(Guid userId)
        {
            try
            {
                string cacheKey = string.Format(CACHE_KEY_DASHBOARD, userId.ToString());

                return await GetOrCreateCachedItemAsync(
                    cacheKey,
                    async () =>
                    {
                        // Run these tasks in parallel for performance
                        var totalInvestmentsTask = FetchTotalInvestmentsAsync(userId);
                        var assetHoldingsTask = FetchAssetHoldingsAsync(userId, AssetType.Exchange);

                        // Wait for both tasks to complete
                        await Task.WhenAll(totalInvestmentsTask, assetHoldingsTask);

                        // Get the results
                        var totalInvestmentsResult = await totalInvestmentsTask;
                        var assetHoldingsResult = await assetHoldingsTask;

                        if (!totalInvestmentsResult.IsSuccess || !assetHoldingsResult.IsSuccess)
                        {
                            var errorMessage = !totalInvestmentsResult.IsSuccess
                                ? totalInvestmentsResult.ErrorMessage
                                : assetHoldingsResult.ErrorMessage;

                            throw new Exception($"Failed to fetch dashboard data components: {errorMessage}");
                        }

                        // Calculate portfolio value from holdings
                        var portfolioValue = assetHoldingsResult.Data.Sum(a => a.Value);

                        // Update persisted dashboard data asynchronously without awaiting
                        _ = UpdateDashboardData(
                            userId,
                            totalInvestmentsResult.Data,
                            assetHoldingsResult.Data,
                            portfolioValue);

                        var result = new DashboardDto
                        {
                            AssetHoldings = assetHoldingsResult.Data,
                            TotalInvestments = totalInvestmentsResult.Data,
                            PortfolioValue = portfolioValue
                        };

                        return ResultWrapper<DashboardDto>.Success(result);
                    },
                    TimeSpan.FromMinutes(DASHBOARD_CACHE_MINUTES)
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get dashboard data for user {UserId}: {Message}", userId, ex.Message);
                return ResultWrapper<DashboardDto>.FromException(ex);
            }
        }

        private async Task<ResultWrapper<IEnumerable<AssetHoldingsDto>>> FetchAssetHoldingsAsync(Guid userId, string? assetTypeFilter = null)
        {
            #region Validate

            if (userId == Guid.Empty)
            {
                ResultWrapper<IEnumerable<AssetHoldingsDto>>.Failure(FailureReason.ValidationError, "Invalid user id.");
            }

            if (!string.IsNullOrEmpty(assetTypeFilter) && !AssetType.AllValues.Contains(assetTypeFilter))
            {
                ResultWrapper<IEnumerable<AssetHoldingsDto>>.Failure(FailureReason.ValidationError, "Invalid asset type.");
            }

            #endregion

            try
            {
                _logger.LogInformation("Fetching asset holdings for user {UserId}", userId);

                // Get user balances
                var balances = await _balanceService.FetchBalancesWithAssetsAsync(userId);

                if (balances == null || !balances.Any())
                {
                    _logger.LogInformation("No balances found for user {UserId}", userId);
                    return ResultWrapper<IEnumerable<AssetHoldingsDto>>.Success(new List<AssetHoldingsDto>());
                }

                var result = new List<AssetHoldingsDto>();

                foreach (var balance in balances)
                {
                    try
                    {
                        // Get the current price for this asset
                        var asset = balance.AssetDocs;

                        if (asset == null || string.IsNullOrEmpty(asset.Exchange))
                        {
                            _logger.LogWarning("Missing asset data for balance: {AssetId}", balance.AssetId);
                            continue;
                        }

                        if (asset.Type != assetTypeFilter)
                        {
                            continue;
                        }

                        var rateResult = await _exchangeService.Exchanges[asset.Exchange].GetAssetPrice(asset.Ticker);

                        if (rateResult == null || !rateResult.IsSuccess)
                        {
                            _logger.LogWarning("Failed to fetch {Ticker} value from exchange {Exchange}: {Error}",
                                asset.Ticker, asset.Exchange, rateResult?.ErrorMessage ?? "Asset price fetch returned null");

                            // Use a fallback value of 0 for assets we can't price
                            result.Add(new AssetHoldingsDto
                            {
                                Name = asset.Name,
                                Ticker = asset.Ticker,
                                Symbol = asset.Symbol,
                                Available = balance.Available,
                                Locked = balance.Locked,
                                Total = balance.Total,
                                Value = 0
                            });
                            continue;
                        }

                        // Calculate the value
                        decimal value = balance.Total * rateResult.Data;

                        // Create the holdings DTO
                        result.Add(new AssetHoldingsDto
                        {
                            Name = asset.Name,
                            Ticker = asset.Ticker,
                            Symbol = asset.Symbol,
                            Available = balance.Available,
                            Locked = balance.Locked,
                            Total = balance.Total,
                            Value = value
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing asset holding for balance {AssetId}", balance.AssetId);
                        // Continue with other balances
                    }
                }

                _logger.LogInformation("Retrieved {Count} asset holdings for user {UserId}", result.Count, userId);
                return ResultWrapper<IEnumerable<AssetHoldingsDto>>.Success(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch asset holdings for user {UserId}: {Message}", userId, ex.Message);
                return ResultWrapper<IEnumerable<AssetHoldingsDto>>.FromException(ex);
            }
        }

        private async Task<ResultWrapper<decimal>> FetchTotalInvestmentsAsync(Guid userId)
        {
            try
            {
                _logger.LogInformation("Fetching total investments for user {UserId}", userId);

                var subscriptionResult = await _subscriptionService.GetAllByUserIdAsync(userId);

                if (subscriptionResult == null || !subscriptionResult.IsSuccess || subscriptionResult.Data == null)
                {
                    throw new SubscriptionFetchException(
                        $"Failed to fetch subscriptions for user {userId}: {subscriptionResult?.ErrorMessage ?? "Subscription fetch returned null."}");
                }

                decimal total = subscriptionResult.Data?.Sum(s => s.TotalInvestments) ?? 0m;

                _logger.LogInformation("Total investments for user {UserId}: {Total}", userId, total);
                return ResultWrapper<decimal>.Success(total);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch total investments for user {UserId}: {Message}", userId, ex.Message);
                return ResultWrapper<decimal>.FromException(ex);
            }
        }

        public async Task<ResultWrapper> UpdateDashboardData(
            Guid userId,
            decimal totalInvestments,
            IEnumerable<AssetHoldingsDto> assetHoldings,
            decimal portfolioValue)
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
                    _logger.LogDebug("Updated dashboard data for user {UserId}", userId);
                }
                else
                {
                    // Document doesn't exist, create a new one with explicit Guid ID
                    var dashboard = new DashboardData
                    {
                        Id = Guid.NewGuid(),
                        UserId = userId,
                        TotalInvestments = totalInvestments,
                        AssetHoldings = assetHoldings,
                        PortfolioValue = portfolioValue,
                        LastUpdated = DateTime.UtcNow
                    };

                    await _collection.InsertOneAsync(dashboard);
                    _logger.LogDebug("Created new dashboard data for user {UserId}", userId);
                }

                // Invalidate the cache
                _cache.Remove(string.Format(CACHE_KEY_DASHBOARD, userId));

                return ResultWrapper.Success();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update dashboard data for user {UserId}: {Message}", userId, ex.Message);
                return ResultWrapper.FromException(ex);
            }
        }
    }
}