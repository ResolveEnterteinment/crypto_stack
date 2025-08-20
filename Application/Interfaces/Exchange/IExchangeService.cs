using Application.Interfaces.Base;
using Domain.DTOs;
using Domain.DTOs.Exchange;
using Domain.Models.Exchange;

namespace Application.Interfaces.Exchange
{
    /// <summary>
    /// Service for managing cryptocurrency exchange operations and interactions.
    /// Provides access to configured exchanges and transaction management capabilities.
    /// </summary>
    public interface IExchangeService : IBaseService<ExchangeOrderData>
    {
        /// <summary>
        /// Gets a dictionary of available exchange integrations.
        /// </summary>
        IReadOnlyDictionary<string, IExchange> Exchanges { get; }

        /// <summary>
        /// Gets the default exchange to use when none is specified.
        /// </summary>
        IExchange DefaultExchange { get; }

        Task<ResultWrapper<decimal>> GetCachedAssetPriceAsync(string ticker);

        Task<ResultWrapper<Dictionary<string, decimal>>> GetCachedAssetPricesAsync(IEnumerable<string> tickers);

        /// <summary>
        /// Retrieves all pending exchange orders that need reconciliation.
        /// </summary>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>A collection of pending exchange orders.</returns>
        Task<ResultWrapper<List<ExchangeOrderData>>> GetPendingOrdersAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets exchange orders by various criteria.
        /// </summary>
        /// <param name="userId">Optional user ID filter.</param>
        /// <param name="subscriptionId">Optional subscription ID filter.</param>
        /// <param name="status">Optional status filter.</param>
        /// <param name="assetId">Optional asset ID filter.</param>
        /// <param name="page">Page number (1-based).</param>
        /// <param name="pageSize">Items per page.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>A collection of exchange orders matching the criteria.</returns>
        Task<ResultWrapper<PaginatedResult<ExchangeOrderData>>> GetOrdersAsync(
            Guid? userId = null,
            Guid? subscriptionId = null,
            string status = null,
            Guid? assetId = null,
            int page = 1,
            int pageSize = 20,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets exchange orders by payment provider ID.
        /// </summary>
        /// <param name="paymentProviderId">The payment provider ID to filter by.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>A collection of orders for the payment.</returns>
        Task<ResultWrapper<List<ExchangeOrderData>>> GetOrdersByPaymentProviderIdAsync(
            string paymentProviderId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if an exchange is available and properly configured.
        /// </summary>
        /// <param name="exchangeName">The name of the exchange to check.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>True if the exchange is available.</returns>
        Task<ResultWrapper<bool>> IsExchangeAvailableAsync(
            string exchangeName,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets minimum notional value for a specific asset with caching.
        /// </summary>
        /// <param name="ticker">Asset ticker symbol</param>
        /// <param name="exchange">Exchange name (optional)</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Minimum notional value required for orders</returns>
        Task<ResultWrapper<decimal>> GetMinNotionalAsync(string ticker, string? exchange = null, CancellationToken ct = default);

        /// <summary>
        /// Gets minimum notional values for multiple assets simultaneously with caching.
        /// </summary>
        /// <param name="tickers">Array of asset ticker symbols</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Dictionary mapping tickers to their minimum notional values</returns>
        Task<ResultWrapper<Dictionary<string, decimal>>> GetMinNotionalsAsync(string[] tickers, CancellationToken ct = default);
    }
}