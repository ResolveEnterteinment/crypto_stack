// Application/Interfaces/Exchange/IExchange.cs (updated)
using Domain.DTOs;
using Domain.DTOs.Exchange;

namespace Application.Interfaces.Exchange
{
    /// <summary>
    /// Interface for exchange operations including trading, balance management, and order information
    /// </summary>
    public interface IExchange
    {
        /// <summary>
        /// Gets the name of the exchange
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Gets or sets the reserve asset ticker (e.g., USDT)
        /// </summary>
        string ReserveAssetTicker { get; set; }

        /// <summary>
        /// Places a spot market buy order
        /// </summary>
        /// <param name="symbol">The trading pair symbol</param>
        /// <param name="quantity">The amount to spend in quote currency</param>
        /// <param name="paymentProviderId">Unique payment provider identifier</param>
        /// <returns>The placed order details</returns>
        Task<PlacedExchangeOrder> PlaceSpotMarketBuyOrder(string symbol, decimal quantity, string paymentProviderId);

        /// <summary>
        /// Places a spot market sell order
        /// </summary>
        /// <param name="symbol">The trading pair symbol</param>
        /// <param name="quantity">The amount to sell in base currency</param>
        /// <param name="paymentProviderId">Unique payment provider identifier</param>
        /// <returns>The placed order details</returns>
        Task<PlacedExchangeOrder> PlaceSpotMarketSellOrder(string symbol, decimal quantity, string paymentProviderId);

        /// <summary>
        /// Gets balances for specified tickers or all non-zero balances
        /// </summary>
        /// <param name="tickers">Optional list of specific tickers to get balances for</param>
        /// <returns>Collection of exchange balances</returns>
        Task<ResultWrapper<IEnumerable<ExchangeBalance>>> GetBalancesAsync(IEnumerable<string>? tickers = null);

        /// <summary>
        /// Gets the balance for a specific ticker
        /// </summary>
        /// <param name="ticker">The ticker to get balance for</param>
        /// <returns>The exchange balance</returns>
        Task<ResultWrapper<ExchangeBalance>> GetBalanceAsync(string ticker);

        /// <summary>
        /// Checks if there is enough balance for a specified amount
        /// </summary>
        /// <param name="ticker">The ticker to check</param>
        /// <param name="amount">The amount to check against</param>
        /// <returns>True if enough balance is available, otherwise false</returns>
        Task<ResultWrapper<bool>> CheckBalanceHasEnough(string ticker, decimal amount);

        /// <summary>
        /// Gets information about a specific order
        /// </summary>
        /// <param name="orderId">The order ID to get information for</param>
        /// <returns>The order details</returns>
        Task<PlacedExchangeOrder> GetOrderInfoAsync(long orderId);

        /// <summary>
        /// Gets previous filled orders for a ticker and client order ID
        /// </summary>
        /// <param name="assetTicker">The asset ticker</param>
        /// <param name="clientOrderId">The client order ID</param>
        /// <returns>Collection of filled orders</returns>
        Task<ResultWrapper<IEnumerable<PlacedExchangeOrder>>> GetPreviousFilledOrders(string assetTicker, string clientOrderId);

        /// <summary>
        /// Gets orders by client order ID
        /// </summary>
        /// <param name="ticker">The ticker</param>
        /// <param name="clientOrderId">The client order ID</param>
        /// <returns>Collection of orders</returns>
        Task<ResultWrapper<IEnumerable<PlacedExchangeOrder>>> GetOrdersByClientOrderId(string ticker, string clientOrderId);

        /// <summary>
        /// Gets the current price for an asset
        /// </summary>
        /// <param name="ticker">The ticker to get price for</param>
        /// <returns>The current price</returns>
        Task<ResultWrapper<decimal>> GetAssetPrice(string ticker);

        /// <summary>
        /// Gets the minimum notional value required to place an order for the specified ticker
        /// </summary>
        /// <param name="ticker">The ticker to get minimum notional for</param>
        /// <returns>The minimum notional value required for orders</returns>
        Task<ResultWrapper<decimal>> GetMinNotional(string ticker);

        /// <summary>
        /// Gets minimum notional values for multiple assets simultaneously
        /// </summary>
        /// <param name="tickers">Array of asset tickers to get minimum notionals for</param>
        /// <returns>Dictionary mapping symbols to their minimum notional values</returns>
        Task<ResultWrapper<Dictionary<string, decimal>>> GetMinNotionals(string[] tickers);
    }
}