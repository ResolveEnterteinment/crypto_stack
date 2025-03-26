using Application.Interfaces.Exchange;
using Domain.Constants;
using Domain.DTOs;
using Domain.DTOs.Exchange;
using Domain.Models.Crypto;
using Domain.Models.Payment;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Infrastructure.Services.Exchange
{
    public class OrderManagementService : IOrderManagementService
    {
        private readonly ILogger _logger;
        public OrderManagementService(
            ILogger<OrderManagementService> logger
            )
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<ResultWrapper<PlacedExchangeOrder>> PlaceExchangeOrderAsync(IExchange exchange, string assetTicker, decimal quantity, string paymentProviderId, string side = OrderSide.Buy, string type = "MARKET")
        {
            try
            {
                if (quantity <= 0m)
                {
                    throw new ArgumentOutOfRangeException(nameof(quantity), $"Quantity must be greater than zero. Provided value is {quantity}");
                }

                if (string.IsNullOrEmpty(assetTicker))
                {
                    throw new ArgumentException("Asset ticker cannot be null or empty.", nameof(assetTicker));
                }

                PlacedExchangeOrder placedOrder;
                var symbol = assetTicker + exchange.ReserveAssetTicker;

                if (side == OrderSide.Buy)
                {
                    //Returns BinancePlacedOrder, otherwise throws Exception
                    placedOrder = await exchange.PlaceSpotMarketBuyOrder(symbol, quantity, paymentProviderId);
                }
                else if (side == OrderSide.Sell)
                {
                    //Returns BinancePlacedOrder, otherwise throws Exception
                    placedOrder = await exchange.PlaceSpotMarketSellOrder(symbol, quantity, paymentProviderId);
                }
                else
                {
                    throw new ArgumentException("Invalid order side. Allowed values are BUY or SELL.", nameof(side));
                }

                if (placedOrder.Status == OrderStatus.Failed)
                {
                    return ResultWrapper<PlacedExchangeOrder>.Failure(FailureReason.ExchangeApiError, $"Order failed with status {placedOrder.Status}");
                }

                _logger.LogInformation("Order placed successfully for symbol: {Symbol}, OrderId: {OrderId}, Status: {Status}", symbol, placedOrder.OrderId, placedOrder.Status);
                return ResultWrapper<PlacedExchangeOrder>.Success(placedOrder);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create order: {Message}", ex.Message);
                return ResultWrapper<PlacedExchangeOrder>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<decimal>> GetPreviousOrdersSum(IExchange exchange, AssetData asset, PaymentData payment)
        {
            try
            {
                //Check previous filled orders. If present process only the remaining amount.
                var previousFilledSum = 0m;
                var previousOrdersResult = await exchange.GetPreviousFilledOrders(asset.Ticker, payment.PaymentProviderId);
                if (!previousOrdersResult.IsSuccess || previousOrdersResult.Data is null)
                {
                    throw new Exception($"Failed to fetch previous filled orders for client order id {payment.PaymentProviderId}: {previousOrdersResult.ErrorMessage}");
                }

                if (previousOrdersResult.Data.Any())
                {
                    previousFilledSum = previousOrdersResult.Data
                       .Select(o => o.QuoteQuantityFilled).Sum();
                }
                return ResultWrapper<decimal>.Success(previousFilledSum);
            }
            catch (Exception ex)
            {
                return ResultWrapper<decimal>.FromException(ex);
            }
        }
        public async Task HandleDustAsync(PlacedExchangeOrder order)
        {
            /*
             * Add a clause: “Residual quantities below the exchange’s minimum trade size may be retained by [Platform Name] as part of transaction processing.
             * Users may opt to convert dust to a designated asset (e.g., Platform Coin?) periodically.”
             * Get user consent during signup.
            */
            var dust = order.QuoteQuantity - order.QuoteQuantityFilled;
            if (order.Status == OrderStatus.Filled && dust > 0m)
            {
                await Task.CompletedTask;
            }
            await Task.CompletedTask;
        }
    }
}
