using Domain.DTOs;
using Domain.Interfaces;
using Domain.Models.Exchange;
using MongoDB.Driver;

namespace Application.Interfaces.Exchange
{
    /// <summary>
    /// Service for managing cryptocurrency exchange operations and interactions.
    /// Provides access to configured exchanges and transaction management capabilities.
    /// </summary>
    public interface IExchangeService : IRepository<ExchangeOrderData>
    {
        /// <summary>
        /// Gets a dictionary of available exchange integrations.
        /// </summary>
        IReadOnlyDictionary<string, IExchange> Exchanges { get; }

        /// <summary>
        /// Gets the default exchange to use when none is specified.
        /// </summary>
        IExchange DefaultExchange { get; }

        /// <summary>
        /// Starts a new MongoDB client session for transaction management.
        /// </summary>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>A session handle that can be used for transactions.</returns>
        Task<IClientSessionHandle> StartDBSession(CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves all pending exchange orders that need reconciliation.
        /// </summary>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>A collection of pending exchange orders.</returns>
        Task<ResultWrapper<IEnumerable<ExchangeOrderData>>> GetPendingOrdersAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a new exchange order.
        /// </summary>
        /// <param name="order">The order data to create.</param>
        /// <param name="session">Optional session handle for transaction support.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>The created order with ID.</returns>
        Task<ResultWrapper<ExchangeOrderData>> CreateOrderAsync(
            ExchangeOrderData order,
            IClientSessionHandle session = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Updates the status of an exchange order.
        /// </summary>
        /// <param name="orderId">The ID of the order to update.</param>
        /// <param name="status">The new status.</param>
        /// <param name="quantityFilled">The filled quantity.</param>
        /// <param name="price">The execution price.</param>
        /// <param name="session">Optional session handle for transaction support.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>True if update was successful.</returns>
        Task<ResultWrapper<bool>> UpdateOrderStatusAsync(
            Guid orderId,
            string status,
            decimal? quantityFilled = null,
            decimal? price = null,
            IClientSessionHandle session = null,
            CancellationToken cancellationToken = default);

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
        public Task<ResultWrapper<PaginatedResult<ExchangeOrderData>>> GetOrdersAsync(
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
        Task<ResultWrapper<IEnumerable<ExchangeOrderData>>> GetOrdersByPaymentProviderIdAsync(
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
    }
}