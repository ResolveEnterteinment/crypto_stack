using Application.Interfaces;
using Application.Interfaces.Asset;
using Application.Interfaces.Base;
using Application.Interfaces.Exchange;
using Application.Interfaces.Logging;
using Application.Interfaces.Payment;
using Application.Interfaces.Subscription;
using Domain.Constants;
using Domain.DTOs;
using Domain.DTOs.Dashboard;
using Domain.DTOs.Payment;
using Domain.Events;
using Domain.Events.Entity;
using Domain.Exceptions;
using Domain.Models.Dashboard;
using Domain.Models.Subscription;
using Infrastructure.Hubs;
using Infrastructure.Services.Base;
using Microsoft.AspNetCore.SignalR;
using MongoDB.Driver;

namespace Infrastructure.Services
{
    public class DashboardService :
        BaseService<DashboardData>,
        IDashboardService
    {
        private readonly IExchangeService _exchangeService;
        private readonly ISubscriptionService _subscriptionService;
        private readonly IPaymentService _paymentService;
        private readonly IBalanceService _balanceService;
        private readonly IHubContext<DashboardHub> _hubContext;

        private const string CACHE_KEY = "dashboard:{0}";
        private static readonly TimeSpan CACHE_DURATION = TimeSpan.FromMinutes(2);

        public DashboardService(
            ICrudRepository<DashboardData> repository,
            ICacheService<DashboardData> cacheService,
            IMongoIndexService<DashboardData> indexService,
            ILoggingService logger,
            IEventService eventService,
            IExchangeService exchangeService,
            ISubscriptionService subscriptionService,
            IPaymentService paymentService,
            IAssetService assetService,
            IBalanceService balanceService,
            IHubContext<DashboardHub> hubContext
        ) : base(
            repository,
            cacheService,
            indexService,
            logger,
            eventService,
            new[]
            {
                new CreateIndexModel<DashboardData>(
                    Builders<DashboardData>.IndexKeys.Ascending(d => d.UserId),
                    new CreateIndexOptions { Name = "UserId_1", Unique = true }
                )
            }
        )
        {
            _exchangeService = exchangeService ?? throw new ArgumentNullException(nameof(exchangeService));
            _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
            _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
            _balanceService = balanceService ?? throw new ArgumentNullException(nameof(balanceService));
            _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
        }

        public Task<ResultWrapper<DashboardDto>> GetDashboardDataAsync(Guid userId)
        {
            string key = string.Format(CACHE_KEY, userId);
            return FetchCached(
                key,
                async () =>
                {
                    var inv = FetchTotalInvestmentsAsync(userId);
                    var holdings = FetchAssetHoldingsAsync(userId, AssetType.Exchange);
                    await Task.WhenAll(inv, holdings);
                    var invRes = await inv;
                    var holdRes = await holdings;
                    if (!invRes.IsSuccess) throw new Exception(invRes.ErrorMessage);
                    if (!holdRes.IsSuccess) throw new Exception(holdRes.ErrorMessage);
                    decimal total = invRes.Data;
                    var assets = holdRes.Data;
                    decimal portfolio = assets.Sum(a => a.Value);
                    // Persist update
                    _ = UpdateDashboardData(userId, total, assets, portfolio);
                    var dto = new DashboardDto { TotalInvestments = total, AssetHoldings = assets, PortfolioValue = portfolio };
                    return dto;
                },
                CACHE_DURATION,
                () => new KeyNotFoundException($"User {userId} Dashboard data not found")
            );
        }

        private async Task<ResultWrapper<IEnumerable<AssetHoldingsDto>>> FetchAssetHoldingsAsync(Guid userId, string filterType)
        {
            try
            {
                if (userId == Guid.Empty) throw new ArgumentException("Invalid userId");
                var balancesResult = await _balanceService.FetchBalancesWithAssetsAsync(userId);
                if (balancesResult == null || !balancesResult.IsSuccess)
                    throw new BalanceFetchException($"Failed to fetch user {userId} balances.");
                var balances = balancesResult.Data;
                var list = new List<AssetHoldingsDto>();
                foreach (var b in balances)
                {
                    var asset = b.AssetDocs;
                    if (asset.Type != filterType) continue;
                    var rate = await _exchangeService.Exchanges[asset.Exchange].GetAssetPrice(asset.Ticker);
                    decimal val = rate.IsSuccess ? b.Total * rate.Data : 0;
                    list.Add(new AssetHoldingsDto
                    {
                        Id = asset.Id.ToString(),
                        Name = asset.Name,
                        Symbol = asset.Symbol,
                        Ticker = asset.Ticker,
                        Total = b.Total,
                        Value = val
                    });
                }
                return ResultWrapper<IEnumerable<AssetHoldingsDto>>.Success(list);
            }
            catch (Exception ex)
            {
                return ResultWrapper<IEnumerable<AssetHoldingsDto>>.FromException(ex);
            }
        }

        private async Task<ResultWrapper<decimal>> FetchTotalInvestmentsAsync(Guid userId)
        {
            try
            {
                var subs = await _subscriptionService.GetAllByUserIdAsync(userId);
                if (!subs.IsSuccess) throw new SubscriptionFetchException(subs.ErrorMessage);
                decimal total = subs.Data.Sum(s => s.TotalInvestments);
                return ResultWrapper<decimal>.Success(total);
            }
            catch (Exception ex)
            {
                return ResultWrapper<decimal>.FromException(ex);
            }
        }

        public async Task<ResultWrapper> UpdateDashboardData(Guid userId, decimal total, IEnumerable<AssetHoldingsDto> assets, decimal portfolio)
        {
            try
            {
                // Upsert by userId
                var filter = Builders<DashboardData>.Filter.Eq(d => d.UserId, userId);
                var existingWr = await GetOneAsync(filter);
                var fields = new { TotalInvestments = total, AssetHoldings = assets, PortfolioValue = portfolio, LastUpdated = DateTime.UtcNow };
                if (existingWr.Data != null)
                    await UpdateAsync(existingWr.Data.Id, fields);
                else
                {
                    var newData = new DashboardData { UserId = userId, TotalInvestments = total, AssetHoldings = assets, PortfolioValue = portfolio, LastUpdated = DateTime.UtcNow };
                    await InsertAsync(newData);
                }
                CacheService.Invalidate(string.Format(CACHE_KEY, userId));
                return ResultWrapper.Success();
            }
            catch (Exception ex)
            {
                return ResultWrapper.FromException(ex);
            }
        }

        public Task Handle(PaymentReceivedEvent notification, CancellationToken ct)
            => InvalidateCacheAndPush(notification.Payment.UserId);

        public Task Handle(EntityCreatedEvent<SubscriptionData> notification, CancellationToken ct)
            => InvalidateCacheAndPush(notification.Entity.UserId);

        public async Task InvalidateDashboardCacheAsync(Guid userId)
        {
            string key = string.Format(CACHE_KEY, userId);
            CacheService.Invalidate(key);
        }

        public async Task<ResultWrapper<SubscriptionPaymentStatusDto>> GetSubscriptionPaymentStatusAsync(Guid subscriptionId)
        {
            using var scope = Logger.BeginScope("DashboardService::GetSubscriptionPaymentStatusAsync", new Dictionary<string, object>
            {
                ["SubscriptionId"] = subscriptionId
            });

            try
            {
                // Get subscription
                var subscriptionResult = await _subscriptionService.GetByIdAsync(subscriptionId);
                if (!subscriptionResult.IsSuccess || subscriptionResult.Data == null)
                {
                    throw new KeyNotFoundException($"Subscription {subscriptionId} not found");
                }

                var subscription = subscriptionResult.Data;

                // Get latest payment
                var latestPaymentResult = await _paymentService.GetLatestPaymentAsync(subscriptionId);

                // Get failed payment count
                var failedPaymentCountResult = await _paymentService.GetFailedPaymentCountAsync(subscriptionId);

                var paymentStatus = new SubscriptionPaymentStatusDto
                {
                    SubscriptionId = subscriptionId,
                    Status = subscription.Status,
                    FailedPaymentCount = failedPaymentCountResult.IsSuccess ? failedPaymentCountResult.Data : 0,
                    LatestPayment = latestPaymentResult.IsSuccess && latestPaymentResult.Data != null
                        ? new PaymentDto(latestPaymentResult.Data)
                        : null
                };

                return ResultWrapper<SubscriptionPaymentStatusDto>.Success(paymentStatus);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error getting subscription payment status for {subscriptionId}: {ex.Message}");
                return ResultWrapper<SubscriptionPaymentStatusDto>.FromException(ex);
            }
        }
        private async Task InvalidateCacheAndPush(Guid userId)
        {
            string key = string.Format(CACHE_KEY, userId);
            CacheService.Invalidate(key);
            var dashWr = await GetDashboardDataAsync(userId);
            if (dashWr.IsSuccess)
                await _hubContext.Clients.Group(userId.ToString()).SendAsync("DashboardUpdate", dashWr.Data);
        }
    }
}
