using Application.Interfaces;
using AspNetCore.Identity.MongoDbCore.Infrastructure;
using BinanceLibrary;
using Domain.Constants;
using Domain.DTOs;
using Domain.Events;
using Domain.Exceptions;
using Domain.Models.Balance;
using Domain.Models.Event;
using Domain.Models.Exchange;
using Domain.Models.Payment;
using Domain.Models.Transaction;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Polly;

namespace Infrastructure.Services
{
    public class ExchangeService : BaseService<ExchangeOrderData>, IExchangeService, INotificationHandler<PaymentReceivedEvent>
    {
        private readonly string _reserveStableAssetTicker;
        protected IBinanceService _binanceService;

        private readonly IEventService _eventService;
        private readonly ISubscriptionService _subscriptionService;
        private readonly IBalanceService _balanceService;
        private readonly ITransactionService _transactionService;
        private readonly IAssetService _assetService;

        public ExchangeService(
            IOptions<ExchangeSettings> exchangeSettings,
            IOptions<BinanceSettings> binanceSettings,
            IOptions<MongoDbSettings> mongoDbSettings,
            IMongoClient mongoClient,
            IEventService eventService,
            ISubscriptionService subscriptionService,
            IBalanceService balanceService,
            ITransactionService transactionService,
            IAssetService assetService,
            ILogger<ExchangeService> logger) : base(mongoClient, mongoDbSettings, "exchange_orders", logger)
        {
            _reserveStableAssetTicker = exchangeSettings.Value.ReserveStableAssetTicker;
            _binanceService = new BinanceService(binanceSettings, logger); //CreateBinanceService(binanceSettings, logger);
            _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
            _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
            _balanceService = balanceService ?? throw new ArgumentNullException(nameof(balanceService));
            _transactionService = transactionService ?? throw new ArgumentNullException(nameof(transactionService));
            _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
        }

        protected virtual IBinanceService CreateBinanceService(IOptions<BinanceSettings> settings, ILogger logger, IBinanceService? refService = null)
        {
            return refService ?? new BinanceService(settings, logger);
        }

        public async Task Handle(PaymentReceivedEvent paymentNotification, CancellationToken cancellationToken)
        {
            _logger.LogInformation($"Notification {paymentNotification.GetType().Name} received with payment id #{paymentNotification.Payment} and event id #{paymentNotification.EventId}");
            try
            {
                var payment = paymentNotification.Payment;
                if (payment == null)
                {
                    _logger.LogWarning($"Invalid payment data. Payment can not be null: {paymentNotification.Payment}");
                    return;
                }

                // Define Polly retry policy
                var policy = Policy
                    .Handle<Exception>()
                    .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                        onRetry: (ex, time, retryCount, context) =>
                            _logger.LogWarning("Retry {RetryCount} for PaymentId: {PaymentId} due to {Exception}",
                                retryCount, paymentNotification.Payment, ex.Message));

                // Execute with retries
                await policy.ExecuteAsync(async () =>
                {
                    var result = await ProcessPayment(payment);
                    if (!result.IsSuccess)
                    {
                        throw new Exception($"Order processing failed: {string.Join(", ", result.Data.Where(o => !o.IsSuccess).Select(o => $"Asset id #{o.CoinId}: {o.ErrorMessage}"))}");
                    }
                });

                // Mark event as processed
                await _eventService.UpdateOneAsync(paymentNotification.EventId, new
                {
                    Processed = true,
                    ProcessedAt = DateTime.UtcNow
                });

                _logger.LogInformation("Successfully processed payment: {PaymentId}", paymentNotification.Payment.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to process PaymentReceivedEvent for PaymentId: {paymentNotification.Payment} after retries: {ex.Message}");
                // Event remains unprocessed in MongoDB for recovery
            }
        }

        public async Task<ResultWrapper<PlacedExchangeOrder>> PlaceExchangeOrderAsync(Guid assetId, string assetTicker, decimal quantity, string paymentProviderId, string side = OrderSide.Buy, string type = "MARKET")
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
                var symbol = assetTicker + "USDT";

                if (side == OrderSide.Buy)
                {
                    //Returns BinancePlacedOrder, otherwise throws Exception
                    placedOrder = await _binanceService.PlaceSpotMarketBuyOrder(symbol, quantity, paymentProviderId);
                }
                else if (side == OrderSide.Sell)
                {
                    //Returns BinancePlacedOrder, otherwise throws Exception
                    placedOrder = await _binanceService.PlaceSpotMarketSellOrder(symbol, quantity, paymentProviderId);
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
                return ResultWrapper<PlacedExchangeOrder>.Failure(FailureReason.From(ex), $"{ex.Message}");
            }
        }

        public async Task<ResultWrapper<IEnumerable<OrderResult>>> ProcessPayment(PaymentData payment)
        {
            var orderResults = new List<OrderResult>();
            var netAmount = payment.NetAmount;

            try
            {
                if (netAmount <= 0m)
                {
                    throw new ArgumentOutOfRangeException($"Invalid transaction net amount. Must be greater than zero. Provided value is {netAmount}");
                }

                // Check exchange fiat balance

                var checkBlanceResult = await CheckExchangeBalanceAsync(netAmount);
                if (!checkBlanceResult.IsSuccess)
                {
                    throw new MongoException(checkBlanceResult.ErrorMessage);
                }
                if (checkBlanceResult.Data is false)
                {
                    //RequestFunding();
                    _logger.LogWarning("Insufficient balance for {Required}", netAmount);
                    var storedEvent = new EventData
                    {
                        EventType = typeof(RequestfundingEvent).Name,
                        Payload = netAmount.ToString()
                    };
                    var storedEventResult = await _eventService.InsertOneAsync(storedEvent);
                    if (!storedEventResult.IsAcknowledged || storedEventResult.InsertedId is null)
                    {
                        throw new MongoException($"Failed to store {storedEvent.EventType} event data with payload {storedEvent.Payload}.");
                    }
                    await _eventService.Publish(new RequestfundingEvent(netAmount, storedEventResult.InsertedId.Value));
                    throw new InsufficientBalanceException($"Insufficient balance. Unable to process payment {payment.Id}.");
                }

                var fetchAllocationsResult = await _subscriptionService.GetAllocationsAsync(payment.SubscriptionId);
                if (!fetchAllocationsResult.IsSuccess || fetchAllocationsResult.Data is null || !fetchAllocationsResult.Data.Any())
                {
                    orderResults.Add(OrderResult.Failure(null, FailureReason.ValidationError, $"Unable to fetch asset allocations: {fetchAllocationsResult.ErrorMessage}"));
                    throw new MongoException($"Unable to fetch asset allocations: {fetchAllocationsResult.ErrorMessage}");
                }

                foreach (var alloc in fetchAllocationsResult.Data)
                {
                    try
                    {
                        if (alloc.PercentAmount <= 0 || alloc.PercentAmount > 100)
                            throw new ArgumentOutOfRangeException(nameof(alloc.PercentAmount), "Allocation must be between 0-100.");

                        decimal quoteOrderQuantity = netAmount * (alloc.PercentAmount / 100m);
                        if (quoteOrderQuantity <= 0m)
                            throw new ArgumentException($"Quote order quantity must be positive. Value: {quoteOrderQuantity}");

                        //First check if there are previous orders already processed with this payment in the exchange.
                        var previousFilledSum = 0m;
                        var previousOrdersResult = await GetPreviousFilledOrders(alloc.AssetTicker, payment.PaymentProviderId);
                        if (!previousOrdersResult.IsSuccess || previousOrdersResult.Data is null)
                        {
                            throw new Exception($"Failed to fetch previous filled orders: {previousOrdersResult.ErrorMessage}");
                        }

                        if (previousOrdersResult.Data.Any())
                        {
                            previousFilledSum = previousOrdersResult.Data
                               .Select(o => o.QuoteQuantityFilled).Sum();
                        }

                        _logger.LogInformation($"Previous orders sum: {previousFilledSum}");
                        var remainingQuoteOrderQuantity = quoteOrderQuantity - previousFilledSum;

                        var symbol = alloc.AssetTicker + "USDT";
                        var placedOrderResult = await PlaceExchangeOrderAsync(alloc.AssetId, alloc.AssetTicker, remainingQuoteOrderQuantity, payment.PaymentProviderId);
                        if (!placedOrderResult.IsSuccess || placedOrderResult.Data is null || placedOrderResult.Data.Status == OrderStatus.Failed)
                            throw new Exception($"Unable to place order: {placedOrderResult.ErrorMessage}");
                        var placedOrder = placedOrderResult.Data;
                        var dust = placedOrder.QuoteQuantity - placedOrder.QuoteQuantityFilled;
                        if (placedOrder.Status == OrderStatus.Filled && dust > 0m)
                        {
                            await HandleDustAsync(placedOrder);
                        }
                        // Atomic insert of transaction and event
                        using (var session = await _mongoClient.StartSessionAsync())
                        {
                            session.StartTransaction();
                            try
                            {
                                var insertOrderResult = await InsertOneAsync(new ExchangeOrderData()
                                {
                                    UserId = payment.UserId,
                                    PaymentProviderId = payment.PaymentProviderId,
                                    SubscriptionId = payment.SubscriptionId,
                                    PlacedOrderId = placedOrder?.OrderId,
                                    AssetId = alloc.AssetId,
                                    QuoteQuantity = placedOrder.QuoteQuantity,
                                    QuoteQuantityFilled = placedOrder.QuoteQuantityFilled,
                                    QuoteQuantityDust = dust,
                                    Quantity = placedOrder.QuantityFilled,
                                    Price = placedOrder.Price,
                                    Status = placedOrder.Status
                                }, session);

                                if (!insertOrderResult.IsAcknowledged)
                                {
                                    //TO-DO: Fallback save ExchangeOrderData locally to reconcile later.
                                    _logger.LogError($"Failed to create order record: {insertOrderResult?.ErrorMessage}");
                                    throw new MongoException($"Failed to create order record: {insertOrderResult?.ErrorMessage}");
                                }

                                var updateBalanceResult = await _balanceService.UpsertBalanceAsync(insertOrderResult.InsertedId.Value, payment.UserId, new BalanceData()
                                {
                                    UserId = payment.UserId,
                                    AssetId = alloc.AssetId,
                                    Available = placedOrder.QuantityFilled
                                }, session);

                                if (updateBalanceResult is null || !updateBalanceResult.IsSuccess)
                                {
                                    //TO-DO: Fallback save update BalanceData locally to reconcile later.
                                    _logger.LogError($"Failed to update balances: {updateBalanceResult.ErrorMessage}");
                                    throw new MongoException($"Failed to update balances: {updateBalanceResult?.ErrorMessage}");
                                }

                                var insertTransactionResult = await _transactionService.InsertOneAsync(new TransactionData()
                                {
                                    UserId = payment.UserId,
                                    PaymentProviderId = payment.PaymentProviderId,
                                    SubscriptionId = payment.SubscriptionId,
                                    BalanceId = updateBalanceResult.Data.Id,
                                    SourceName = "Exchange",
                                    SourceId = placedOrder.OrderId.ToString(),
                                    Action = "Buy",
                                    Quantity = placedOrder.QuantityFilled,
                                }, session);

                                if (!insertTransactionResult.IsAcknowledged)
                                {
                                    //TO-DO: Fallback save ExchangeOrderData locally to reconcile later.
                                    _logger.LogError($"Failed to create transaction record: {insertTransactionResult?.ErrorMessage}");
                                    throw new MongoException($"Failed to create transaction record: {insertTransactionResult?.ErrorMessage}");
                                }

                                await session.CommitTransactionAsync();
                            }
                            catch (Exception ex)
                            {
                                await session.AbortTransactionAsync();
                                _logger.LogError(ex, "Failed to atomically insert exchange order and transaction.");
                            }
                            orderResults.Add(OrderResult.Success(
                                    placedOrder.OrderId,
                                    alloc.AssetId.ToString(),
                                    placedOrder.QuoteQuantity,
                                    placedOrder.QuantityFilled,
                                    placedOrder.Status
                                    ));
                        }
                        _logger.LogInformation("Order created for {Symbol}, OrderId: {OrderId}", symbol, placedOrder.OrderId);
                    }
                    catch (Exception ex)
                    {
                        orderResults.Add(OrderResult.Failure(alloc?.AssetId.ToString(), FailureReason.From(ex), ex.Message));
                        _logger.LogError(ex, "Failed to process order: {Message}", ex.Message);
                    }
                }
                return ResultWrapper<IEnumerable<OrderResult>>.Success(orderResults);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process payment: {Message}", ex.Message);
                return ResultWrapper<IEnumerable<OrderResult>>.Failure(FailureReason.From(ex), $"{ex.Message}");
            }
        }

        public async Task<ResultWrapper<IEnumerable<PlacedExchangeOrder>>> GetPreviousFilledOrders(string assetTicker, string paymentProviderId)
        {
            var ordersResult = await _binanceService.GetOrdersByClientOrderId(assetTicker, paymentProviderId);
            if (ordersResult == null || !ordersResult.IsSuccess || ordersResult.Data == null)
            {
                return ResultWrapper<IEnumerable<PlacedExchangeOrder>>.Failure(FailureReason.ExchangeApiError, ordersResult.ErrorMessage);
            }
            var filledOrders = ordersResult.Data.Where(o => o.Status == OrderStatus.Filled || o.Status == OrderStatus.PartiallyFilled);

            return ResultWrapper<IEnumerable<PlacedExchangeOrder>>.Success(filledOrders);
        }

        public async Task<ResultWrapper<bool>> CheckExchangeBalanceAsync(decimal amount)
        {
            // Check exchange fiat balance
            var balanceData = await _binanceService.GetBalanceAsync(_reserveStableAssetTicker);
            if (!balanceData.IsSuccess)
            {
                return ResultWrapper<bool>.Failure(FailureReason.DatabaseError, "Unable to fetch exchange balances.");
            }
            decimal fiatBalance = balanceData.Data?.Available ?? 0m;
            return ResultWrapper<bool>.Success(fiatBalance >= amount);
        }

        // Reconciliation method for WebSocket reliability
        public async Task ReconcilePendingOrdersAsync()
        {
            var filter = Builders<ExchangeOrderData>.Filter.Eq(o => o.Status, OrderStatus.Pending);
            var pendingOrders = await GetAllAsync(filter);

            foreach (var order in pendingOrders)
            {
                try
                {
                    if (order.PlacedOrderId is null) throw new ArgumentNullException(nameof(order.PlacedOrderId));
                    PlacedExchangeOrder exchangeOrder = await _binanceService.GetOrderInfoAsync((long)order.PlacedOrderId); // TO-DO: Create GetOrderStatus dunction

                    if (exchangeOrder is null) throw new ArgumentNullException(nameof(exchangeOrder));

                    var update = Builders<ExchangeOrderData>.Update
                        .Set(o => o.Status, exchangeOrder.Status.ToString()) // Map Binance status to your enum/string
                        .Set(o => o.Quantity, exchangeOrder.QuantityFilled)
                        .Set(o => o.Price, exchangeOrder.Price);

                    await UpdateOneAsync(order.Id, new
                    {
                        Status = exchangeOrder.Status.ToString(),
                        Quantity = exchangeOrder.QuantityFilled,
                        Price = exchangeOrder.Price
                    });

                    if (exchangeOrder.Status == OrderStatus.Failed)
                        await HandleFailedOrderAsync(order);
                    else if (exchangeOrder.Status == OrderStatus.PartiallyFilled)
                        await HandlePartiallyFilledOrderAsync(order);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to reconcile order {OrderId}: {Message}", order.PlacedOrderId, ex.Message);
                }
            }
        }

        private async Task HandleDustAsync(PlacedExchangeOrder order)
        {
            /*
             * Add a clause: “Residual quantities below the exchange’s minimum trade size may be retained by [Platform Name] as part of transaction processing.
             * Users may opt to convert dust to a designated asset (e.g., Platform Coin?) periodically.”
             * Get user consent during signup.
            */
            await Task.CompletedTask;
        }

        private async Task HandleFailedOrderAsync(ExchangeOrderData order)
        {
            if (order.RetryCount >= 3)
            {
                _logger.LogError("Max retries reached for OrderId: {OrderId}", order.PlacedOrderId);
                await UpdateOneAsync(order.Id, new
                {
                    Status = OrderStatus.Failed
                });
                return;
            }

            var retryOrder = new ExchangeOrderData
            {
                UserId = order.UserId,
                PaymentProviderId = order.PaymentProviderId,
                SubscriptionId = order.SubscriptionId,
                AssetId = order.AssetId,
                QuoteQuantity = order.QuoteQuantity,
                QuoteQuantityDust = 0,
                Status = OrderStatus.Queued,
                PreviousOrderId = order.Id,
                RetryCount = order.RetryCount + 1
            };
            await EnqueuOrderAsync(retryOrder);
            _logger.LogInformation("Queued retry order for failed OrderId: {OrderId}", order.PlacedOrderId);
        }

        private async Task HandlePartiallyFilledOrderAsync(ExchangeOrderData order)
        {
            if (order.Quantity is null) throw new ArgumentNullException(nameof(order.Quantity));
            if (order.QuoteQuantityFilled is null) throw new ArgumentNullException(nameof(order.QuoteQuantityFilled));

            decimal remainingQty = (decimal)(order.QuoteQuantity - order.QuoteQuantityFilled);
            if (remainingQty > 0m)
            {
                var retryOrder = new ExchangeOrderData
                {
                    UserId = order.UserId,
                    PaymentProviderId = order.PaymentProviderId,
                    SubscriptionId = order.SubscriptionId,
                    AssetId = order.AssetId,
                    QuoteQuantity = remainingQty,
                    QuoteQuantityDust = 0,
                    Status = OrderStatus.Queued,
                    RetryCount = order.RetryCount + 1,
                    PreviousOrderId = order.Id
                };
                await EnqueuOrderAsync(retryOrder); //TO-DO: Create EnqueueOrderAsync function
                _logger.LogInformation("Queued partial fill order for OrderId: {OrderId}, RemainingQty: {Remaining}", order.PlacedOrderId, remainingQty);
            }
        }

        private async Task EnqueuOrderAsync(ExchangeOrderData order)
        {
            await InsertOneAsync(order);
        }

        // New method to reset balances to USDT
        public async Task<ResultWrapper<bool>> ResetBalances(IEnumerable<string>? tickers = null)
        {
            var orderResults = new List<OrderResult>();

            try
            {
                _logger.LogInformation("Starting balance reset to USDT");

                // Get current Testnet balances
                var balancesResult = await _binanceService.GetBalancesAsync(tickers);
                var USDTBalance = balancesResult.Data.Where(b => b.Asset == "USDT").Select(b => b.Available).FirstOrDefault();
                _logger.LogInformation($"USDT Balance before reset: {USDTBalance}");
                if (!balancesResult.IsSuccess || balancesResult.Data == null)
                {
                    _logger.LogError("Failed to retrieve Testnet balances: {Error}", balancesResult.ErrorMessage);
                    return ResultWrapper<bool>.Failure(FailureReason.ExchangeApiError, $"Failed to retrieve balances: {balancesResult.ErrorMessage}");
                }

                var balances = balancesResult.Data;
                if (!balances.Any())
                {
                    _logger.LogInformation("No balances to reset");
                    return ResultWrapper<bool>.Success(true);
                }

                // Process each non-USDT asset
                foreach (var balance in balances.Where(b => b.Asset != "USDT" && b.Available > 0m))
                {
                    try
                    {
                        var assetResult = await _assetService.GetByTickerAsync(balance.Asset);
                        if (!assetResult.IsSuccess)
                        {
                            orderResults.Add(OrderResult.Failure(balance.Asset, assetResult.FailureReason, $"Failed to get {balance.Asset} data: {assetResult.ErrorMessage}"));
                            continue;
                        }

                        // Sell the entire available quantity to USDT
                        var symbol = $"{balance.Asset}USDT";
                        var sellOrderResult = await PlaceExchangeOrderAsync(
                            assetResult.Data.Id,
                            assetResult.Data.Ticker,
                            balance.Available,
                            string.Empty, // No subscription ID needed for reset
                            OrderSide.Sell,
                            "MARKET"
                        );

                        if (!sellOrderResult.IsSuccess || sellOrderResult.Data == null)
                        {
                            orderResults.Add(OrderResult.Failure(
                                assetResult.Data.ToString(),
                                sellOrderResult.FailureReason,
                                $"Failed to sell {balance.Asset}: {sellOrderResult.ErrorMessage}"
                            ));
                            continue;
                        }

                        var order = sellOrderResult.Data;
                        var dust = order.QuoteQuantity - order.QuoteQuantityFilled;
                        var orderData = new ExchangeOrderData
                        {
                            UserId = Guid.Empty, // System-initiated reset, no user
                            PaymentProviderId = string.Empty, // No specific transaction
                            SubscriptionId = Guid.Empty,
                            PlacedOrderId = order.OrderId,
                            AssetId = assetResult.Data.Id,
                            QuoteQuantity = order.QuoteQuantity,
                            QuoteQuantityFilled = order.QuoteQuantityFilled,
                            QuoteQuantityDust = dust,
                            Quantity = order.QuantityFilled,
                            Price = order.Price,
                            Status = order.Status
                        };

                        // Record the sell order
                        var insertResult = await InsertOneAsync(orderData);
                        if (!insertResult.IsAcknowledged)
                        {
                            orderResults.Add(OrderResult.Failure(
                                balance.Asset,
                                FailureReason.DatabaseError,
                                $"Failed to record order for {balance.Asset}: {insertResult.ErrorMessage}"
                            ));
                            continue;
                        }

                        orderResults.Add(OrderResult.Success(
                            order.OrderId,
                            assetResult.Data.Id.ToString(),
                            order.QuoteQuantity,
                            order.QuantityFilled,
                            order.Status
                        ));
                        _logger.LogInformation("Sold {Ticker} to USDT: OrderId {OrderId}", balance.Asset, order.OrderId);
                    }
                    catch (Exception ex)
                    {
                        orderResults.Add(OrderResult.Failure(
                            balance.Asset,
                            FailureReason.Unknown,
                            $"Error selling {balance.Asset}: {ex.Message}"
                        ));
                        _logger.LogError(ex, "Failed to reset balance for {Ticker}", balance.Asset);
                    }
                }
                balancesResult = await _binanceService.GetBalancesAsync(tickers);
                USDTBalance = balancesResult.Data.Where(b => b.Asset == "USDT").Select(b => b.Available).FirstOrDefault();
                // Check if all operations succeeded
                if (orderResults.All(r => r.IsSuccess))
                {
                    _logger.LogInformation($"Successfully reset all balances to USDT. USDT Balance after reset: {USDTBalance}");
                    return ResultWrapper<bool>.Success(true);
                }
                else
                {
                    var errors = string.Join("; ", orderResults.Where(r => !r.IsSuccess).Select(r => r.ErrorMessage));
                    _logger.LogWarning($"Partial failure in resetting balances: {errors}. USDT Balance after reset: {USDTBalance}");
                    return ResultWrapper<bool>.Failure(FailureReason.Unknown, $"Partial failure: {errors}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reset balances to USDT");
                return ResultWrapper<bool>.Failure(FailureReason.Unknown, $"Failed to reset balances: {ex.Message}");
            }
        }
    }
}