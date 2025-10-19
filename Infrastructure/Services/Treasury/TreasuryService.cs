using Application.Interfaces;
using Application.Interfaces.Asset;
using Application.Interfaces.Base;
using Application.Interfaces.Exchange;
using Application.Interfaces.Logging;
using Application.Interfaces.Treasury;
using Domain.Constants.Treasury;
using Domain.DTOs;
using Domain.DTOs.Treasury;
using Domain.Exceptions;
using Domain.Models.Treasury;
using Infrastructure.Services.Base;
using MongoDB.Driver;
using System.Text;

namespace Infrastructure.Services.Treasury
{
    /// <summary>
    /// Service for managing corporate treasury operations
    /// Tracks all revenue from fees, dust, rounding, and other sources
    /// </summary>
    public class TreasuryService : BaseService<TreasuryTransactionData>, ITreasuryService
    {
        private readonly ITreasuryBalanceService _balanceService;
        private readonly IAssetService _assetService;
        //private readonly IExchangeService _exchangeService;
        private readonly IUserService _userService;
        private readonly ILoggingService _loggingService;
        private readonly ICacheService<TreasuryBalanceData> _cacheService;

        private const string BALANCE_CACHE_PREFIX = "treasury:balance:";
        private const string SUMMARY_CACHE_PREFIX = "treasury:summary:";
        private static readonly TimeSpan CACHE_DURATION = TimeSpan.FromMinutes(5);

        public TreasuryService(
            IServiceProvider serviceProvider,
            ITreasuryBalanceService treasuryBalanceService,
            IAssetService assetService,
            //IExchangeService exchangeService,
            IUserService userService,
            ILoggingService loggingService,
            ICacheService<TreasuryBalanceData> cacheService
        ) : base(
            serviceProvider,
            new()
            {
                PublishCRUDEvents = true,
                IndexModels = [
                    new CreateIndexModel<TreasuryTransactionData>(
                        Builders<TreasuryTransactionData>.IndexKeys
                            .Ascending(t => t.AssetTicker)
                            .Ascending(t => t.CreatedAt),
                        new CreateIndexOptions { Name = "AssetTicker_CreatedAt" }),
                    new CreateIndexModel<TreasuryTransactionData>(
                        Builders<TreasuryTransactionData>.IndexKeys
                            .Ascending(t => t.TransactionType)
                            .Ascending(t => t.Source),
                        new CreateIndexOptions { Name = "Type_Source" }),
                    new CreateIndexModel<TreasuryTransactionData>(
                        Builders<TreasuryTransactionData>.IndexKeys
                            .Ascending(t => t.UserId),
                        new CreateIndexOptions { Name = "UserId_1" }),
                    new CreateIndexModel<TreasuryTransactionData>(
                        Builders<TreasuryTransactionData>.IndexKeys
                            .Ascending(t => t.Status)
                            .Ascending(t => t.CreatedAt),
                        new CreateIndexOptions { Name = "Status_CreatedAt" }),
                    new CreateIndexModel<TreasuryTransactionData>(
                        Builders<TreasuryTransactionData>.IndexKeys
                            .Ascending(t => t.IsReported)
                            .Ascending(t => t.ReportingPeriod),
                        new CreateIndexOptions { Name = "Reporting" }),
                    new CreateIndexModel<TreasuryTransactionData>(
                        Builders<TreasuryTransactionData>.IndexKeys
                            .Ascending(t => t.Exchange)
                            .Ascending(t => t.OrderId),
                        new CreateIndexOptions { Name = "Exchange_Order", Sparse = true })
                ]
            })
        {
            _balanceService = treasuryBalanceService ?? throw new ArgumentNullException(nameof(treasuryBalanceService));
            _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
            //_exchangeService = exchangeService ?? throw new ArgumentNullException(nameof(exchangeService));
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        }

        #region Transaction Recording

        public async Task<TreasuryTransactionData> RecordTransactionAsync(
            string transactionType,
            string source,
            decimal amount,
            string assetTicker,
            Guid assetId,
            TreasuryTransactionMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Validate inputs
                ValidateTransactionInputs(transactionType, source, amount, assetTicker, assetId);

                // Calculate USD value if exchange rate provided
                decimal? usdValue = null;
                if (metadata.ExchangeRate.HasValue && metadata.ExchangeRate.Value > 0)
                {
                    usdValue = amount * metadata.ExchangeRate.Value;
                }

                // Create transaction record
                var transaction = new TreasuryTransactionData
                {
                    Id = Guid.NewGuid(),
                    TransactionType = transactionType,
                    Source = source,
                    Amount = amount,
                    AssetTicker = assetTicker,
                    AssetId = assetId,
                    UserId = metadata.UserId,
                    SubscriptionId = metadata.SubscriptionId,
                    RelatedTransactionId = metadata.RelatedTransactionId,
                    RelatedEntityType = metadata.RelatedEntityType,
                    Exchange = metadata.Exchange,
                    OrderId = metadata.OrderId,
                    ExchangeRate = metadata.ExchangeRate,
                    UsdValue = usdValue,
                    Description = metadata.Description,
                    Metadata = metadata.AdditionalData,
                    Status = TreasuryTransactionStatus.Collected,
                    CollectedAt = DateTime.UtcNow,
                    IsReported = false,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                // Save transaction
                await InsertAsync(transaction, cancellationToken);

                // Update balance
                await _balanceService.UpdateBalanceAsync(transaction, cancellationToken);

                InvalidateSummaryCache();

                _loggingService.LogInformation(
                    "Treasury transaction recorded: {Type} - {Source} - {Amount} {Asset}",
                    transactionType, source, amount, assetTicker);

                return transaction;
            }
            catch (Exception ex)
            {
                _loggingService.LogError(
                    "Error recording treasury transaction: {Error}",
                    ex.Message);
                throw;
            }
        }

        public async Task<TreasuryTransactionData> RecordPlatformFeeAsync(
            decimal amount,
            string assetTicker,
            Guid assetId,
            Guid userId,
            Guid? subscriptionId,
            string relatedTransactionId,
            CancellationToken cancellationToken = default)
        {
            var metadata = new TreasuryTransactionMetadata
            {
                UserId = userId,
                SubscriptionId = subscriptionId,
                RelatedTransactionId = relatedTransactionId,
                RelatedEntityType = TreasuryRelatedEntityType.Payment,
                Description = $"Platform fee (1%) collected from user transaction"
            };

            return await RecordTransactionAsync(
                TreasuryTransactionType.Fee,
                TreasuryTransactionSource.PlatformFee,
                amount,
                assetTicker,
                assetId,
                metadata,
                cancellationToken);
        }

        public async Task<TreasuryTransactionData> RecordDustCollectionAsync(
            decimal dustAmount,
            string assetTicker,
            Guid assetId,
            string exchange,
            string orderId,
            Guid? userId,
            CancellationToken cancellationToken = default)
        {
            // Only record if dust amount is significant enough
            if (dustAmount <= 0 || dustAmount < 0.00000001m)
            {
                await _loggingService.LogTraceAsync(
                    $"Dust amount {dustAmount} {assetTicker} too small to record",level: Domain.Constants.Logging.LogLevel.Warning);
                return null;
            }

            var metadata = new TreasuryTransactionMetadata
            {
                UserId = userId,
                Exchange = exchange,
                OrderId = orderId,
                RelatedEntityType = TreasuryRelatedEntityType.Order,
                Description = $"Dust collected from {exchange} order {orderId}"
            };

            return await RecordTransactionAsync(
                TreasuryTransactionType.Dust,
                TreasuryTransactionSource.OrderDust,
                dustAmount,
                assetTicker,
                assetId,
                metadata,
                cancellationToken);
        }

        public async Task<TreasuryTransactionData> RecordRoundingDifferenceAsync(
            decimal roundingAmount,
            string assetTicker,
            Guid assetId,
            string source,
            string relatedTransactionId,
            CancellationToken cancellationToken = default)
        {
            // Only record if rounding is significant
            if (Math.Abs(roundingAmount) <= 0.000001m)
            {
                return null;
            }

            var metadata = new TreasuryTransactionMetadata
            {
                RelatedTransactionId = relatedTransactionId,
                Description = $"Rounding difference from {source}"
            };

            return await RecordTransactionAsync(
                TreasuryTransactionType.Rounding,
                TreasuryTransactionSource.OrderRounding,
                Math.Abs(roundingAmount),
                assetTicker,
                assetId,
                metadata,
                cancellationToken);
        }

        #endregion

        #region Reporting

        public async Task<TreasurySummaryDto> GetSummaryAsync(
            DateTime startDate,
            DateTime endDate,
            CancellationToken cancellationToken = default)
        {
            // Try cache
            var cacheKey = $"{SUMMARY_CACHE_PREFIX}{startDate:yyyyMMdd}_{endDate:yyyyMMdd}";

            var transactions = await _cacheService.GetAnyCachedAsync(cacheKey, async () =>
            {
                var filter = Builders<TreasuryTransactionData>.Filter.And(
                Builders<TreasuryTransactionData>.Filter.Gte(t => t.CreatedAt, startDate.AddDays(-1)),
                Builders<TreasuryTransactionData>.Filter.Lte(t => t.CreatedAt, endDate.AddDays(1)),
                Builders<TreasuryTransactionData>.Filter.Eq(t => t.Status, TreasuryTransactionStatus.Collected)
            );

                var transactionsResult = await GetManyAsync(filter, cancellationToken);

                if (transactionsResult == null || !transactionsResult.IsSuccess || transactionsResult.Data == null)
                    throw new DatabaseException("Failed to fetch treasury transactions");

                return transactionsResult.Data;
                
            }, CACHE_DURATION) ?? [];

            var summary = new TreasurySummaryDto
            {
                StartDate = startDate,
                EndDate = endDate,
                TotalTransactions = transactions.Count,
                TotalUsdValue = transactions.Sum(t => t.UsdValue ?? 0),
                TotalPlatformFees = transactions
                        .Where(t => t.TransactionType == TreasuryTransactionType.Fee)
                        .Sum(t => t.UsdValue ?? 0),
                TotalDustCollected = transactions
                        .Where(t => t.TransactionType == TreasuryTransactionType.Dust)
                        .Sum(t => t.UsdValue ?? 0),
                TotalRounding = transactions
                        .Where(t => t.TransactionType == TreasuryTransactionType.Rounding)
                        .Sum(t => t.UsdValue ?? 0),
                TotalOther = transactions
                        .Where(t => t.TransactionType == TreasuryTransactionType.Other)
                        .Sum(t => t.UsdValue ?? 0)
            };

            // Asset balances
            summary.AssetBalances = transactions
                .GroupBy(t => t.AssetTicker)
                .Select(g => new AssetBalanceSummary
                {
                    AssetTicker = g.Key,
                    Balance = g.Sum(t => t.Amount),
                    UsdValue = g.Sum(t => t.UsdValue ?? 0),
                    PlatformFeeBalance = g.Where(t => t.TransactionType == TreasuryTransactionType.Fee).Sum(t => t.Amount),
                    DustBalance = g.Where(t => t.TransactionType == TreasuryTransactionType.Dust).Sum(t => t.Amount),
                    RoundingBalance = g.Where(t => t.TransactionType == TreasuryTransactionType.Rounding).Sum(t => t.Amount)
                })
                .OrderByDescending(a => a.UsdValue)
                .ToList();

            // Daily breakdown
            summary.DailyBreakdown = transactions
                .GroupBy(t => t.CreatedAt.Date)
                .Select(g => new DailyRevenue
                {
                    Date = g.Key,
                    TotalUsd = g.Sum(t => t.UsdValue ?? 0),
                    PlatformFees = g.Where(t => t.TransactionType == TreasuryTransactionType.Fee).Sum(t => t.UsdValue ?? 0),
                    Dust = g.Where(t => t.TransactionType == TreasuryTransactionType.Dust).Sum(t => t.UsdValue ?? 0),
                    Rounding = g.Where(t => t.TransactionType == TreasuryTransactionType.Rounding).Sum(t => t.UsdValue ?? 0),
                    TransactionCount = g.Count()
                })
                .OrderBy(d => d.Date)
                .ToList();

            return summary;
        }

        public async Task<List<TreasuryBreakdownDto>> GetBreakdownBySourceAsync(
            DateTime startDate,
            DateTime endDate,
            CancellationToken cancellationToken = default)
        {
            var filter = Builders<TreasuryTransactionData>.Filter.And(
                Builders<TreasuryTransactionData>.Filter.Gte(t => t.CreatedAt, startDate),
                Builders<TreasuryTransactionData>.Filter.Lte(t => t.CreatedAt, endDate),
                Builders<TreasuryTransactionData>.Filter.Eq(t => t.Status, TreasuryTransactionStatus.Collected)
            );

            var transactionsResult = await GetManyAsync(filter, cancellationToken);

            if (transactionsResult == null || !transactionsResult.IsSuccess || transactionsResult.Data == null)
                throw new DatabaseException("Failed to fetch treasury transactions");

            var transactions = transactionsResult.Data;

            return transactions
                .GroupBy(t => new { t.Source, t.TransactionType })
                .Select(g => new TreasuryBreakdownDto
                {
                    Source = g.Key.Source,
                    TransactionType = g.Key.TransactionType,
                    TotalAmount = g.Sum(t => t.Amount),
                    TotalUsdValue = g.Sum(t => t.UsdValue ?? 0),
                    TransactionCount = g.Count(),
                    AssetBreakdown = g.GroupBy(t => t.AssetTicker)
                        .Select(ag => new AssetAmount
                        {
                            AssetTicker = ag.Key,
                            Amount = ag.Sum(t => t.Amount),
                            UsdValue = ag.Sum(t => t.UsdValue ?? 0)
                        })
                        .ToList()
                })
                .OrderByDescending(b => b.TotalUsdValue)
                .ToList();
        }

        public async Task<PaginatedResult<TreasuryTransactionData>> GetTransactionHistoryAsync(
            TreasuryTransactionFilter filter,
            int page = 1,
            int pageSize = 50,
            CancellationToken cancellationToken = default)
        {
            var filterBuilder = Builders<TreasuryTransactionData>.Filter;
            var filters = new List<FilterDefinition<TreasuryTransactionData>>();

            if (filter.StartDate.HasValue)
                filters.Add(filterBuilder.Gte(t => t.CreatedAt, filter.StartDate.Value));

            if (filter.EndDate.HasValue)
                filters.Add(filterBuilder.Lte(t => t.CreatedAt, filter.EndDate.Value));

            if (!string.IsNullOrEmpty(filter.TransactionType))
                filters.Add(filterBuilder.Eq(t => t.TransactionType, filter.TransactionType));

            if (!string.IsNullOrEmpty(filter.Source))
                filters.Add(filterBuilder.Eq(t => t.Source, filter.Source));

            if (!string.IsNullOrEmpty(filter.AssetTicker))
                filters.Add(filterBuilder.Eq(t => t.AssetTicker, filter.AssetTicker));

            if (filter.UserId.HasValue)
                filters.Add(filterBuilder.Eq(t => t.UserId, filter.UserId.Value));

            if (!string.IsNullOrEmpty(filter.Status))
                filters.Add(filterBuilder.Eq(t => t.Status, filter.Status));

            if (!string.IsNullOrEmpty(filter.Exchange))
                filters.Add(filterBuilder.Eq(t => t.Exchange, filter.Exchange));

            if (filter.IsReported.HasValue)
                filters.Add(filterBuilder.Eq(t => t.IsReported, filter.IsReported.Value));

            if (!string.IsNullOrEmpty(filter.ReportingPeriod))
                filters.Add(filterBuilder.Eq(t => t.ReportingPeriod, filter.ReportingPeriod));

            if (filter.MinAmount.HasValue)
                filters.Add(filterBuilder.Gte(t => t.Amount, filter.MinAmount.Value));

            if (filter.MaxAmount.HasValue)
                filters.Add(filterBuilder.Lte(t => t.Amount, filter.MaxAmount.Value));

            var combinedFilter = filters.Any() 
                ? filterBuilder.And(filters) 
                : filterBuilder.Empty;

            var sortDefinition = Builders<TreasuryTransactionData>.Sort.Descending(t => t.CreatedAt);

            var totalCount = await _repository.CountAsync(combinedFilter, cancellationToken);

            var paginatedTransactions = await _repository.GetPaginatedAsync(
                combinedFilter,
                sortDefinition,
                page,
                pageSize,
                cancellationToken);

            return paginatedTransactions;
        }

        public async Task<byte[]> ExportTransactionsAsync(
            DateTime startDate,
            DateTime endDate,
            string format = "csv",
            CancellationToken cancellationToken = default)
        {
            var filter = Builders<TreasuryTransactionData>.Filter.And(
                Builders<TreasuryTransactionData>.Filter.Gte(t => t.CreatedAt, startDate),
                Builders<TreasuryTransactionData>.Filter.Lte(t => t.CreatedAt, endDate)
            );

            var transactionsResult = await GetManyAsync(filter, cancellationToken);

            if (transactionsResult == null || !transactionsResult.IsSuccess || transactionsResult.Data == null)
                throw new DatabaseException("Failed to fetch treasury transactions");

            var transactions = transactionsResult.Data;

            if (format.ToLower() == "csv")
            {
                return GenerateCsv(transactions);
            }

            throw new NotSupportedException($"Export format '{format}' is not supported");
        }

        #endregion

        #region Compliance & Audit

        public async Task MarkAsReportedAsync(
            DateTime startDate,
            DateTime endDate,
            string reportingPeriod,
            CancellationToken cancellationToken = default)
        {
            var filter = Builders<TreasuryTransactionData>.Filter.And(
                Builders<TreasuryTransactionData>.Filter.Gte(t => t.CreatedAt, startDate),
                Builders<TreasuryTransactionData>.Filter.Lte(t => t.CreatedAt, endDate),
                Builders<TreasuryTransactionData>.Filter.Eq(t => t.IsReported, false)
            );

            var update = Builders<TreasuryTransactionData>.Update
                .Set(t => t.IsReported, true)
                .Set(t => t.ReportingPeriod, reportingPeriod)
                .Set(t => t.UpdatedAt, DateTime.UtcNow);

            var result = await UpdateManyAsync(filter, update, cancellationToken);

            if (result == null || !result.IsSuccess)
                throw new DatabaseException($"Failed to mark treasury transactions as reported: {result.ErrorMessage}");

            _loggingService.LogInformation(
                "Marked {Count} treasury transactions as reported for period {Period}",
                result.Data.ModifiedCount, reportingPeriod);
        }

        public async Task<List<TreasuryAuditEntry>> GetAuditTrailAsync(
            Guid transactionId,
            CancellationToken cancellationToken = default)
        {
            // TODO: Implement audit trail tracking
            // This would require a separate audit log collection
            return new List<TreasuryAuditEntry>();
        }

        public async Task<TreasuryValidationResult> ValidateBalanceIntegrityAsync(
            string assetTicker,
            CancellationToken cancellationToken = default)
        {
            var result = new TreasuryValidationResult { IsValid = true };

            try
            {
                // Get recorded balance
                var balance = await _balanceService.GetBalanceByAssetAsync(assetTicker, cancellationToken);
                if (balance == null)
                {
                    result.Issues.Add($"No balance record found for {assetTicker}");
                    result.IsValid = false;
                    return result;
                }

                result.RecordedBalance = balance.TotalBalance;

                // Calculate balance from transactions
                var filter = Builders<TreasuryTransactionData>.Filter.And(
                    Builders<TreasuryTransactionData>.Filter.Eq(t => t.AssetTicker, assetTicker),
                    Builders<TreasuryTransactionData>.Filter.Eq(t => t.Status, TreasuryTransactionStatus.Collected)
                );

                var transactionsResult = await GetManyAsync(filter, cancellationToken);

                if (transactionsResult == null || !transactionsResult.IsSuccess || transactionsResult.Data == null)
                    throw new DatabaseException("Failed to fetch treasury transactions");

                var transactions = transactionsResult.Data;

                result.CalculatedBalance = transactions.Sum(t => t.Amount);

                // Check for discrepancy
                result.Difference = result.RecordedBalance - result.CalculatedBalance;
                
                if (Math.Abs(result.Difference) > 0.00000001m)
                {
                    result.IsValid = false;
                    result.Issues.Add(
                        $"Balance mismatch: Recorded={result.RecordedBalance}, " +
                        $"Calculated={result.CalculatedBalance}, " +
                        $"Difference={result.Difference}");
                }

                // Check for negative balance
                if (result.RecordedBalance < 0)
                {
                    result.Warnings.Add($"Negative balance detected: {result.RecordedBalance}");
                }

                return result;
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.Issues.Add($"Validation error: {ex.Message}");
                return result;
            }
        }

        #endregion

        #region Operations

        public async Task<int> ProcessPendingTransactionsAsync(
            CancellationToken cancellationToken = default)
        {
            var filter = Builders<TreasuryTransactionData>.Filter.Eq(
                t => t.Status,
                TreasuryTransactionStatus.Pending);

            var pendingTransactionsResult = await GetManyAsync(filter, cancellationToken);

            if (pendingTransactionsResult == null || !pendingTransactionsResult.IsSuccess || pendingTransactionsResult.Data == null)
                throw new DatabaseException("Failed to fetch pending treasury transactions");

            var pendingTransactions = pendingTransactionsResult.Data;

            int processedCount = 0;

            foreach (var transaction in pendingTransactions)
            {
                try
                {
                    // Update status to collected
                    transaction.Status = TreasuryTransactionStatus.Collected;
                    transaction.CollectedAt = DateTime.UtcNow;
                    transaction.UpdatedAt = DateTime.UtcNow;

                    await UpdateAsync(transaction.Id, transaction, cancellationToken);

                    // Update balance
                    await _balanceService.UpdateBalanceAsync(transaction, cancellationToken);

                    InvalidateSummaryCache();

                    processedCount++;
                }
                catch (Exception ex)
                {
                    _loggingService.LogError(
                        "Error processing pending transaction {Id}: {Error}",
                        transaction.Id, ex.Message);
                }
            }

            _loggingService.LogInformation(
                "Processed {Count} pending treasury transactions",
                processedCount);

            return processedCount;
        }

        public async Task<TreasuryTransactionData> ReverseTransactionAsync(
            Guid transactionId,
            string reason,
            CancellationToken cancellationToken = default)
        {
            var transactionResult = await GetByIdAsync(transactionId, cancellationToken);
            if (transactionResult == null || !transactionResult.IsSuccess || transactionResult.Data == null)
            {
                throw new ResourceNotFoundException($"Treasury transaction", transactionId.ToString());
            }

            var transaction = transactionResult.Data;

            if (transaction.Status == TreasuryTransactionStatus.Reversed)
            {
                throw new InvalidOperationException("Transaction is already reversed");
            }

            // Create reversal transaction
            var reversal = new TreasuryTransactionData
            {
                Id = Guid.NewGuid(),
                TransactionType = transaction.TransactionType,
                Source = transaction.Source,
                Amount = transaction.Amount,
                AssetTicker = transaction.AssetTicker,
                AssetId = transaction.AssetId,
                UserId = transaction.UserId,
                SubscriptionId = transaction.SubscriptionId,
                RelatedTransactionId = transactionId.ToString(),
                RelatedEntityType = "TreasuryReversal",
                Exchange = transaction.Exchange,
                OrderId = transaction.OrderId,
                ExchangeRate = transaction.ExchangeRate,
                UsdValue = transaction.UsdValue.HasValue ? -transaction.UsdValue.Value : null,
                Description = $"Reversal: {reason}",
                Status = TreasuryTransactionStatus.Reversed,
                CollectedAt = DateTime.UtcNow,
                AuditNotes = reason,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            // Save reversal
            await InsertAsync(reversal, cancellationToken);

            // Update original transaction
            transaction.Status = TreasuryTransactionStatus.Reversed;
            transaction.AuditNotes = $"Reversed: {reason}";
            transaction.UpdatedAt = DateTime.UtcNow;
            await UpdateAsync(transactionId, transactionResult, cancellationToken);

            // Update balance
            await _balanceService.UpdateBalanceAsync(reversal, cancellationToken);

            InvalidateSummaryCache();

            _loggingService.LogWarning(
                "Treasury transaction {Id} reversed: {Reason}",
                transactionId, reason);

            return reversal;
        }

        #endregion

        #region Private Helpers

        private void ValidateTransactionInputs(
            string transactionType,
            string source,
            decimal amount,
            string assetTicker,
            Guid assetId)
        {

            var validationErrors = new Dictionary<string, string[]>();

            if (string.IsNullOrWhiteSpace(transactionType))
                validationErrors["Transaction Type"].Append("Transaction type is required");

            if (string.IsNullOrWhiteSpace(source))
                validationErrors["Source"].Append("Source is required");

            if (amount <= 0)
                validationErrors["Amount"].Append("Amount must be greater than zero");

            if (string.IsNullOrWhiteSpace(assetTicker))
                validationErrors["Asset Ticker"].Append("Asset ticker is required");

            if (assetId == Guid.Empty)
                validationErrors["Asset ID"].Append("Asset ID is required");

            if (validationErrors.Count > 0) 
                throw new ValidationException("Failed to validate transaction inputs", validationErrors);
        }

        private void InvalidateSummaryCache()
        {
            _cacheService.InvalidateWithPrefix(SUMMARY_CACHE_PREFIX);
        }

        private byte[] GenerateCsv(List<TreasuryTransactionData> transactions)
        {
            var csv = new StringBuilder();
            
            // Header
            csv.AppendLine("TransactionId,Date,Type,Source,Amount,Asset,UsdValue,Status,UserId,Exchange,OrderId,Description");

            // Data rows
            foreach (var t in transactions)
            {
                csv.AppendLine(string.Join(",",
                    t.Id,
                    t.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    t.TransactionType,
                    t.Source,
                    t.Amount,
                    t.AssetTicker,
                    t.UsdValue?.ToString() ?? "",
                    t.Status,
                    t.UserId?.ToString() ?? "",
                    t.Exchange ?? "",
                    t.OrderId ?? "",
                    $"\"{t.Description?.Replace("\"", "\"\"")}\""));
            }

            return Encoding.UTF8.GetBytes(csv.ToString());
        }

        #endregion
    }
}
