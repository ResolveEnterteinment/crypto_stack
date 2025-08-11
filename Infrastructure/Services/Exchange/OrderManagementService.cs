// Improved and refactored OrderManagementService
using Application.Interfaces.Base;
using Application.Interfaces.Exchange;
using Application.Interfaces.Logging;
using Domain.Constants;
using Domain.Constants.Logging;
using Domain.DTOs;
using Domain.DTOs.Exchange;
using Domain.DTOs.Logging;
using Domain.Exceptions;
using Domain.Models.Asset;
using Domain.Models.Payment;
using MongoDB.Driver;

namespace Infrastructure.Services.Exchange
{
    public class OrderManagementService : IOrderManagementService
    {
        private readonly IResilienceService<PlacedExchangeOrder> _resilienceService;
        private readonly ILoggingService _logger;

        public OrderManagementService(
            IResilienceService<PlacedExchangeOrder> resilienceService,
            ILoggingService logger)
        {
            _resilienceService = resilienceService ?? throw new ArgumentNullException(nameof(resilienceService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Places an exchange order
        /// </summary>
        public Task<ResultWrapper<PlacedExchangeOrder>> PlaceExchangeOrderAsync(
            IExchange exchange,
            string assetTicker,
            decimal quantity,
            string paymentProviderId,
            string side = OrderSide.Buy,
            string type = "MARKET")
        {
            return _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Exchange",
                    FileName = "OrderManagementService",
                    OperationName = "PlaceExchangeOrderAsync(IExchange exchange, string assetTicker, decimal quantity, string paymentProviderId, string side = Domain.Constants.OrderSide.Buy, string type = \"MARKET\")",
                    State = new()
                    {
                        ["Exchange"] = exchange.Name,
                        ["Ticker"] = assetTicker,
                        ["Quantity"] = quantity,
                        ["Side"] = side,
                        ["PaymentProviderId"] = paymentProviderId,
                        ["Type"] = type,

                    },
                    LogLevel = LogLevel.Critical,
                },
                async () =>
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
                    var symbol = assetTicker + exchange.QuoteAssetTicker;
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
                    if (placedOrder == null || placedOrder.Status == OrderStatus.Failed)
                    {
                        throw new OrderExecutionException($"Order failed with status {placedOrder?.Status ?? "Null"}", exchange.Name, placedOrder.OrderId.ToString());
                    }

                    // Log success
                    _logger.LogInformation(
                        "Order placed successfully for symbol: {Symbol}, OrderId: {OrderId}, Status: {Status}",
                        symbol, placedOrder.OrderId, placedOrder.Status);

                    return placedOrder;
                })
                .WithExchangeOperationResilience()
                .WithPerformanceMonitoring(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30))
                .OnSuccess(HandleDustAsync)
                .ExecuteAsync();            
        }

        /// <summary>
        /// Gets the sum of previous orders for a payment
        /// </summary>
        public Task<ResultWrapper<decimal>> GetPreviousOrdersSum(
            IExchange exchange,
            AssetData asset,
            PaymentData payment)
        {
            return _resilienceService.CreateBuilder(
                 new Scope
                 {
                     NameSpace = "Infrastructure.Services.Exchange",
                     FileName = "OrderManagementService",
                     OperationName = "GetPreviousOrdersSum( IExchange exchange, AssetData asset, PaymentData payment)",
                     State = new()
                     {
                         ["Exchange"] = exchange.Name,
                         ["Asset"] = asset.Name,
                         ["PaymentId"] = payment.Id,

                     },
                     LogLevel = LogLevel.Critical,
                 },
                 async () =>
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

                     return previousFilledSum;
                 })
                .WithExchangeOperationResilience()
                .WithPerformanceMonitoring(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30))
                .ExecuteAsync();
        }

        public Task<ResultWrapper<decimal>> GetMinNotional(IExchange exchange, AssetData asset)
        {
            return _resilienceService.CreateBuilder(
                 new Scope
                 {
                     NameSpace = "Infrastructure.Services.Exchange",
                     FileName = "OrderManagementService",
                     OperationName = "GetMinNotional( IExchange exchange, AssetData asset)",
                     State = new()
                     {
                         ["Exchange"] = exchange.Name,
                         ["Asset"] = asset.Name,

                     }
                 },
                 async () =>
                 {
                     // Input validation
                     if (exchange == null)
                         throw new ArgumentNullException(nameof(exchange));

                     if (asset == null)
                         throw new ArgumentNullException(nameof(asset));

                     // Log the operation
                     _logger.LogInformation(
                         "Checking min notional for asset {Ticker}",
                         asset.Ticker, asset.Ticker);

                     // Get previous filled orders
                     var previousFilledSum = 0m;
                     var minNotionaResult = await exchange.GetMinNotional(asset.Ticker);

                     if (minNotionaResult == null || !minNotionaResult.IsSuccess)
                     {
                         throw new Exception(
                             $"Failed to fetch min notional for asset {asset.Ticker}: " +
                             $"{minNotionaResult?.ErrorMessage ?? "Min notional fetch result returned null" }");
                     }

                     return minNotionaResult.Data;
                 })
                .WithExchangeOperationResilience()
                .WithPerformanceMonitoring(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10))
                .ExecuteAsync();
        }

        /// <summary>
        /// Handles dust (small leftover amounts) from exchange orders
        /// </summary>
        private async Task HandleDustAsync(PlacedExchangeOrder order)
        {
            /*
             * Add a clause: "Residual quantities below the exchange's minimum trade size may be retained by [Platform Name] 
             * as part of transaction processing."
             * Users may opt to convert dust to a designated asset (e.g., Platform Coin?) periodically."
             * Get user consent during signup.
            */

            await _resilienceService.CreateBuilder<bool>(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Exchange",
                    FileName = "OrderManagementService",
                    OperationName = "HandleDustAsync(PlacedExchangeOrder order)",
                    State = new()
                    {
                        ["Exchange"] = order.Exchange,
                        ["OrderId"] = order.OrderId,
                        ["DustQuantity"] = order.QuoteQuantity - order.QuantityFilled,

                    }
                },
                async () =>
                {
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
                        _logger.LogError("Error handling dust for order {OrderId}: {ErrorMessage}", order.OrderId, ex.Message);
                        // Don't throw exception from dust handling - it's not critical
                        return false;
                    }
                    return true;
                })
                .WithExchangeOperationResilience()
                .WithPerformanceMonitoring(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10))
                .ExecuteAsync();
        }
    }
}