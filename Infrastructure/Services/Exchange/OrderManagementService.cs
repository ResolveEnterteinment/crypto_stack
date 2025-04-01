// Improved and refactored OrderManagementService
using Application.Interfaces.Exchange;
using Domain.Constants;
using Domain.DTOs;
using Domain.DTOs.Exchange;
using Domain.Models.Asset;
using Domain.Models.Payment;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Infrastructure.Services.Exchange
{
    public class OrderManagementService : IOrderManagementService
    {
        private readonly ILogger<OrderManagementService> _logger;

        public OrderManagementService(
            ILogger<OrderManagementService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Places an exchange order
        /// </summary>
        public async Task<ResultWrapper<PlacedExchangeOrder>> PlaceExchangeOrderAsync(
            IExchange exchange,
            string assetTicker,
            decimal quantity,
            string paymentProviderId,
            string side = OrderSide.Buy,
            string type = "MARKET")
        {
            try
            {
                // Input validation
                if (quantity <= 0m)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(quantity),
                        $"Quantity must be greater than zero. Provided value is {quantity}");
                }

                if (string.IsNullOrEmpty(assetTicker))
                {
                    throw new ArgumentException("Asset ticker cannot be null or empty.", nameof(assetTicker));
                }

                if (exchange == null)
                {
                    throw new ArgumentNullException(nameof(exchange), "Exchange cannot be null");
                }

                // Log the order attempt
                _logger.LogInformation(
                    "Placing {Side} order for {Quantity} of {AssetTicker} on {Exchange}",
                    side, quantity, assetTicker, exchange.Name);

                // Prepare the symbol
                var symbol = assetTicker + exchange.ReserveAssetTicker;
                PlacedExchangeOrder placedOrder;

                // Execute the order based on side
                if (side == OrderSide.Buy)
                {
                    placedOrder = await exchange.PlaceSpotMarketBuyOrder(symbol, quantity, paymentProviderId);
                }
                else if (side == OrderSide.Sell)
                {
                    placedOrder = await exchange.PlaceSpotMarketSellOrder(symbol, quantity, paymentProviderId);
                }
                else
                {
                    throw new ArgumentException(
                        "Invalid order side. Allowed values are BUY or SELL.",
                        nameof(side));
                }

                // Check for failure
                if (placedOrder.Status == OrderStatus.Failed)
                {
                    return ResultWrapper<PlacedExchangeOrder>.Failure(
                        FailureReason.OrderExecutionFailed,
                        $"Order failed with status {placedOrder.Status}");
                }

                // Log success
                _logger.LogInformation(
                    "Order placed successfully for symbol: {Symbol}, OrderId: {OrderId}, Status: {Status}",
                    symbol, placedOrder.OrderId, placedOrder.Status);

                return ResultWrapper<PlacedExchangeOrder>.Success(placedOrder);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create order: {Message}", ex.Message);
                return ResultWrapper<PlacedExchangeOrder>.FromException(ex);
            }
        }

        /// <summary>
        /// Gets the sum of previous orders for a payment
        /// </summary>
        public async Task<ResultWrapper<decimal>> GetPreviousOrdersSum(
            IExchange exchange,
            AssetData asset,
            PaymentData payment)
        {
            try
            {
                // Input validation
                if (exchange == null)
                    throw new ArgumentNullException(nameof(exchange));

                if (asset == null)
                    throw new ArgumentNullException(nameof(asset));

                if (payment == null)
                    throw new ArgumentNullException(nameof(payment));

                // Log the operation
                _logger.LogInformation(
                    "Checking previous filled orders for asset {Ticker} with payment ID {PaymentId}",
                    asset.Ticker, payment.PaymentProviderId);

                // Get previous filled orders
                var previousFilledSum = 0m;
                var previousOrdersResult = await exchange.GetPreviousFilledOrders(
                    asset.Ticker,
                    payment.PaymentProviderId);

                if (!previousOrdersResult.IsSuccess || previousOrdersResult.Data is null)
                {
                    throw new Exception(
                        $"Failed to fetch previous filled orders for client order id {payment.PaymentProviderId}: " +
                        $"{previousOrdersResult.ErrorMessage}");
                }

                // Calculate the sum of filled quantities
                if (previousOrdersResult.Data.Any())
                {
                    previousFilledSum = previousOrdersResult.Data
                       .Select(o => o.QuoteQuantityFilled)
                       .Sum();

                    _logger.LogInformation(
                        "Found previous orders for payment {PaymentId} with filled sum: {FilledSum}",
                        payment.PaymentProviderId, previousFilledSum);
                }
                else
                {
                    _logger.LogInformation(
                        "No previous orders found for payment {PaymentId}",
                        payment.PaymentProviderId);
                }

                return ResultWrapper<decimal>.Success(previousFilledSum);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error getting previous orders sum for asset {Ticker} with payment ID {PaymentId}",
                    asset?.Ticker, payment?.PaymentProviderId);

                return ResultWrapper<decimal>.FromException(ex);
            }
        }

        /// <summary>
        /// Handles dust (small leftover amounts) from exchange orders
        /// </summary>
        public async Task HandleDustAsync(PlacedExchangeOrder order)
        {
            /*
             * Add a clause: "Residual quantities below the exchange's minimum trade size may be retained by [Platform Name] 
             * as part of transaction processing.
             * Users may opt to convert dust to a designated asset (e.g., Platform Coin?) periodically."
             * Get user consent during signup.
            */

            try
            {
                // Calculate dust amount
                var dust = order.QuoteQuantity - order.QuoteQuantityFilled;

                if (order.Status == OrderStatus.Filled && dust > 0m)
                {
                    _logger.LogInformation(
                        "Handling dust amount {DustAmount} for order {OrderId}",
                        dust, order.OrderId);

                    // TODO: Implement dust handling logic
                    // For now, we're just logging it
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling dust for order {OrderId}", order.OrderId);
                // Don't throw exception from dust handling - it's not critical
            }

            await Task.CompletedTask;
        }
    }
}