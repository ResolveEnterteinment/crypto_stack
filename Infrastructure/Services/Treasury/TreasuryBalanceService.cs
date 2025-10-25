using Application.Interfaces.Asset;
using Application.Interfaces.Exchange;
using Application.Interfaces.Treasury;
using Domain.Constants.Treasury;
using Domain.Exceptions;
using Domain.Models.Treasury;
using Infrastructure.Services.Base;
using MongoDB.Driver;

namespace Infrastructure.Services.Treasury
{
    /// <summary>
    /// Service for managing corporate treasury operations
    /// Tracks all revenue from fees, dust, rounding, and other sources
    /// </summary>
    public class TreasuryBalanceService : BaseService<TreasuryBalanceData>, ITreasuryBalanceService
    {
        private readonly IAssetService _assetService;
        private readonly IExchangeService _exchangeService;

        private const string BALANCE_CACHE_PREFIX = "treasury:balance:";
        private static readonly TimeSpan CACHE_DURATION = TimeSpan.FromMinutes(5);

        public TreasuryBalanceService(
            IServiceProvider serviceProvider,
            IAssetService assetService,
            IExchangeService exchangeService
        ) : base(
            serviceProvider,
            new()
            {
                PublishCRUDEvents = true,
                IndexModels = [
                    new CreateIndexModel<TreasuryBalanceData>(
                        Builders<TreasuryBalanceData>.IndexKeys
                            .Ascending(t => t.AssetTicker)
                            .Ascending(t => t.CreatedAt),
                        new CreateIndexOptions { Name = "AssetTicker_CreatedAt" }),
                    new CreateIndexModel<TreasuryBalanceData>(
                        Builders<TreasuryBalanceData>.IndexKeys
                            .Ascending(t => t.Exchange)
                            .Ascending(t => t.CreatedAt),
                        new CreateIndexOptions { Name = "Exchange_CreatedAt" }),

                    new CreateIndexModel<TreasuryBalanceData>(
                        Builders<TreasuryBalanceData>.IndexKeys
                            .Ascending(t => t.IsAvailableForWithdrawal)
                            .Ascending(t => t.CreatedAt),
                        new CreateIndexOptions { Name = "IsAvailableForWithdrawal_CreatedAt"})
                ]
            })
        {
            _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
            _exchangeService = exchangeService ?? throw new ArgumentNullException(nameof(exchangeService));
        }

        #region Balance Management

        public async Task<TreasuryBalanceData?> GetBalanceByAssetAsync(
            string assetTicker,
            CancellationToken cancellationToken = default)
        {
            // Try cache first
            var cacheKey = $"{BALANCE_CACHE_PREFIX}{assetTicker}";
            return await _cacheService.GetCachedEntityAsync(cacheKey, async () =>
            {
                var filter = Builders<TreasuryBalanceData>.Filter.Eq(b => b.AssetTicker, assetTicker);
                var balanceResult = await GetOneAsync(filter, cancellationToken);

                if(balanceResult == null || !balanceResult.IsSuccess)
                {
                    _loggingService.LogError(
                        "Failed to retrieve treasury balance for asset {Asset}: {Error}",
                        assetTicker, balanceResult?.ErrorMessage);
                    throw new BalanceFetchException($"Failed to retrieve treasury balance for asset {assetTicker}: {balanceResult?.ErrorMessage}");
                }

                return balanceResult.Data;
            }, CACHE_DURATION);
        }

        public async Task<List<TreasuryBalanceData>> GetAllBalancesAsync(
            CancellationToken cancellationToken = default)
        {
            var result = await GetAllAsync();
            if (result == null || !result.IsSuccess)
            {
                throw new DatabaseException($"Failed to retrieve all treasury balances: {result?.ErrorMessage}");
            }
            var balances = result.Data;
            return balances
                .OrderByDescending(b => b.TotalUsdValue)
                .ToList() ?? [];
        }

        public async Task UpdateBalanceAsync(
            TreasuryTransactionData transaction,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var filter = Builders<TreasuryBalanceData>.Filter.Eq(b => b.AssetTicker, transaction.AssetTicker);

                // Get or create balance record
                var balanceResult = await GetOneAsync(filter, cancellationToken);

                if(balanceResult == null || !balanceResult.IsSuccess)
                {
                    throw new DatabaseException($"Failed to retrieve treasury balance for asset {transaction.AssetTicker}: {balanceResult?.ErrorMessage}");
                }

                var balance = balanceResult.Data;

                if (balance == null)
                {
                    balance = new TreasuryBalanceData
                    {
                        Id = Guid.NewGuid(),
                        AssetTicker = transaction.AssetTicker,
                        AssetId = transaction.AssetId,
                        TotalBalance = 0,
                        PlatformFeeBalance = 0,
                        DustBalance = 0,
                        RoundingBalance = 0,
                        OtherBalance = 0,
                        TotalUsdValue = 0,
                        TransactionCount = 0,
                        IsAvailableForWithdrawal = true,
                        LockedAmount = 0,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                }

                // Update balances based on transaction type
                var amountDelta = transaction.Status == TreasuryTransactionStatus.Reversed 
                    ? -transaction.Amount 
                    : transaction.Amount;

                balance.TotalBalance += amountDelta;
                balance.TransactionCount++;
                balance.LastTransactionAt = transaction.CreatedAt;

                // Update specific balance categories
                switch (transaction.Source)
                {
                    case TreasuryTransactionSource.PlatformFee:
                    case TreasuryTransactionSource.TradingFee:
                    case TreasuryTransactionSource.WithdrawalFee:
                    case TreasuryTransactionSource.SubscriptionFee:
                        balance.PlatformFeeBalance += amountDelta;
                        break;

                    case TreasuryTransactionSource.OrderDust:
                    case TreasuryTransactionSource.WithdrawalDust:
                    case TreasuryTransactionSource.ConversionDust:
                        balance.DustBalance += amountDelta;
                        break;

                    case TreasuryTransactionSource.OrderRounding:
                    case TreasuryTransactionSource.PriceRounding:
                    case TreasuryTransactionSource.QuantityRounding:
                        balance.RoundingBalance += amountDelta;
                        break;

                    default:
                        balance.OtherBalance += amountDelta;
                        break;
                }

                // Update USD value if available
                if (transaction.UsdValue.HasValue)
                {
                    balance.TotalUsdValue = transaction.UsdValue.Value * (balance.TotalBalance / transaction.Amount);
                    balance.LastExchangeRate = transaction.ExchangeRate;
                    balance.LastUsdUpdateAt = DateTime.UtcNow;
                }

                balance.UpdatedAt = DateTime.UtcNow;

                // Upsert balance
                var replaceResult = await ReplaceAsync(
                    filter,
                    balance,
                    cancellationToken);

                if (replaceResult == null || !replaceResult.IsSuccess || !replaceResult.Data.IsSuccess)
                {
                    throw new DatabaseException($"Failed to update treasury balance for asset {transaction.AssetTicker}: {replaceResult?.ErrorMessage ?? replaceResult?.Data.ErrorMessage}");
                }

                // Invalidate cache
                InvalidateBalanceCache(transaction.AssetTicker);
            }
            catch (Exception ex)
            {
                _loggingService.LogError(
                    "Error updating treasury balance for {Asset}: {Error}",
                    transaction.AssetTicker, ex.Message);
                throw;
            }
        }

        public async Task RefreshUsdValuesAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var balances = await GetAllBalancesAsync(cancellationToken);

                foreach (var balance in balances)
                {
                    // Get current exchange rate from asset service
                    var asset = await _assetService.GetByTickerAsync(balance.AssetTicker);
                    if (asset == null) continue;

                    // TODO: Get current price from price service
                    // For now, skip if no existing exchange rate

                    var currentRate = 0m;

                    if (!balance.LastExchangeRate.HasValue)
                    {
                        var currentRateResult = await _exchangeService.GetCachedAssetPriceAsync(balance.AssetTicker);
                        if (currentRateResult == null || !currentRateResult.IsSuccess)
                            continue;

                        currentRate = currentRateResult.Data;
                    }

                    var updateFields = new Dictionary<string, object>
                    {
                        ["TotalUsdValue"] = balance.TotalBalance * currentRate,
                        ["LastExchangeRate"] = currentRate,
                        ["LastUsdUpdateAt"] = DateTime.UtcNow,
                    };

                    var updateResult = await _assetService.UpdateAsync(balance.Id, updateFields, cancellationToken);

                    if (updateResult == null || !updateResult.IsSuccess || !updateResult.Data.IsSuccess)
                        throw new DatabaseException($"Failed to refresh USD values for treasury balances: {updateResult.ErrorMessage ?? updateResult.Data.ErrorMessage}");
                }

                _loggingService.LogInformation(
                    "Refreshed USD values for {Count} treasury balances",
                    balances.Count);
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Error refreshing treasury USD values: {Error}", ex.Message);
                throw;
            }
        }

        #endregion

        #region Private Helpers

        private void InvalidateBalanceCache(string assetTicker)
        {
            var balanceCacheKey = $"{BALANCE_CACHE_PREFIX}{assetTicker}";
            _cacheService.Invalidate(balanceCacheKey);
        }

        #endregion
    }
}
