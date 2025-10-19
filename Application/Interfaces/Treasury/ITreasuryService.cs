using Domain.DTOs;
using Domain.DTOs.Treasury;
using Domain.Models.Treasury;

namespace Application.Interfaces.Treasury
{
    /// <summary>
    /// Service for managing corporate treasury operations
    /// </summary>
    public interface ITreasuryService
    {
        #region Transaction Recording

        /// <summary>
        /// Records a treasury transaction (fee, dust, rounding, etc.)
        /// </summary>
        Task<TreasuryTransactionData> RecordTransactionAsync(
            string transactionType,
            string source,
            decimal amount,
            string assetTicker,
            Guid assetId,
            TreasuryTransactionMetadata metadata,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Records a platform fee collection
        /// </summary>
        Task<TreasuryTransactionData> RecordPlatformFeeAsync(
            decimal amount,
            string assetTicker,
            Guid assetId,
            Guid userId,
            Guid? subscriptionId,
            string relatedTransactionId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Records dust collection from an exchange order
        /// </summary>
        Task<TreasuryTransactionData> RecordDustCollectionAsync(
            decimal dustAmount,
            string assetTicker,
            Guid assetId,
            string exchange,
            string orderId,
            Guid? userId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Records rounding differences
        /// </summary>
        Task<TreasuryTransactionData> RecordRoundingDifferenceAsync(
            decimal roundingAmount,
            string assetTicker,
            Guid assetId,
            string source,
            string relatedTransactionId,
            CancellationToken cancellationToken = default);

        #endregion

        #region Reporting

        /// <summary>
        /// Gets treasury summary for a date range
        /// </summary>
        Task<TreasurySummaryDto> GetSummaryAsync(
            DateTime startDate,
            DateTime endDate,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets detailed breakdown by source
        /// </summary>
        Task<List<TreasuryBreakdownDto>> GetBreakdownBySourceAsync(
            DateTime startDate,
            DateTime endDate,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets transaction history with filters
        /// </summary>
        Task<PaginatedResult<TreasuryTransactionData>> GetTransactionHistoryAsync(
            TreasuryTransactionFilter filter,
            int page = 1,
            int pageSize = 50,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Exports treasury data for accounting
        /// </summary>
        Task<byte[]> ExportTransactionsAsync(
            DateTime startDate,
            DateTime endDate,
            string format = "csv",
            CancellationToken cancellationToken = default);

        #endregion

        #region Compliance & Audit

        /// <summary>
        /// Marks transactions as reported for a period
        /// </summary>
        Task MarkAsReportedAsync(
            DateTime startDate,
            DateTime endDate,
            string reportingPeriod,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets audit trail for a specific transaction
        /// </summary>
        Task<List<TreasuryAuditEntry>> GetAuditTrailAsync(
            Guid transactionId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Validates treasury balance integrity
        /// </summary>
        Task<TreasuryValidationResult> ValidateBalanceIntegrityAsync(
            string assetTicker,
            CancellationToken cancellationToken = default);

        #endregion

        #region Operations

        /// <summary>
        /// Processes pending treasury transactions
        /// </summary>
        Task<int> ProcessPendingTransactionsAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Reverses a treasury transaction (for corrections)
        /// </summary>
        Task<TreasuryTransactionData> ReverseTransactionAsync(
            Guid transactionId,
            string reason,
            CancellationToken cancellationToken = default);

        #endregion
    }
}
