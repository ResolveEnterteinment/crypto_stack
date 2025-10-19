using Domain.DTOs;
using Domain.DTOs.Treasury;
using Domain.Models.Treasury;

namespace Application.Interfaces.Treasury
{
    /// <summary>
    /// Service for managing corporate treasury operations
    /// </summary>
    public interface ITreasuryBalanceService
    {
        #region Balance Management

        /// <summary>
        /// Gets current treasury balance for a specific asset
        /// </summary>
        Task<TreasuryBalanceData?> GetBalanceByAssetAsync(
            string assetTicker,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets all treasury balances
        /// </summary>
        Task<List<TreasuryBalanceData>> GetAllBalancesAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Updates treasury balance after a transaction
        /// </summary>
        Task UpdateBalanceAsync(
            TreasuryTransactionData transaction,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Refreshes USD values for all balances
        /// </summary>
        Task RefreshUsdValuesAsync(CancellationToken cancellationToken = default);

        #endregion
    }
}
