using Application.Interfaces;
using AspNetCore.Identity.MongoDbCore.Infrastructure;
//using Binance.Net.Enums;
using Binance.Net.Objects.Models.Spot;
using BinanceLibrary;
using Domain.Constants;
using Domain.DTOs;
using Domain.Events;
using Domain.Models.Balance;
using Domain.Models.Crypto;
using Domain.Models.Exchange;
using Domain.Models.Payment;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Polly;

namespace Infrastructure.Services
{
    public class ExchangeService : BaseService<ExchangeOrderData>, IExchangeService, INotificationHandler<PaymentReceivedEvent>
    {
        protected IBinanceService _binanceService;

        private readonly IEventService _eventService;
        private readonly IPaymentService _paymentService;
        private readonly ISubscriptionService _subscriptionService;
        private readonly IBalanceService _balanceService;
        private readonly IAssetService _assetService;
        private string? MapBinanceStatus(Binance.Net.Enums.OrderStatus status) => status switch
        {
            Binance.Net.Enums.OrderStatus.PendingNew or Binance.Net.Enums.OrderStatus.PendingNew => OrderStatus.Pending,
            Binance.Net.Enums.OrderStatus.Filled => OrderStatus.Filled,
            Binance.Net.Enums.OrderStatus.PartiallyFilled => OrderStatus.PartiallyFilled,
            Binance.Net.Enums.OrderStatus.Canceled or Binance.Net.Enums.OrderStatus.Rejected or Binance.Net.Enums.OrderStatus.Expired => OrderStatus.Failed,
            _ => null
        };

        public ExchangeService(
            IOptions<BinanceSettings> binanceSettings,
            IOptions<MongoDbSettings> mongoDbSettings,
            IMongoClient mongoClient,
            IEventService eventService,
            IPaymentService paymentService,
            ISubscriptionService subscriptionService,
            IBalanceService balanceService,
            IAssetService assetService,
            ILogger<ExchangeService> logger) : base(mongoClient, mongoDbSettings, "exchange_orders", logger)
        {
            _binanceService = new BinanceService(binanceSettings, logger); //CreateBinanceService(binanceSettings, logger);
            _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
            _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
            _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
            _balanceService = balanceService ?? throw new ArgumentNullException(nameof(balanceService));
            _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
        }

        protected virtual IBinanceService CreateBinanceService(IOptions<BinanceSettings> settings, ILogger logger, IBinanceService? refService = null)
        {
            return refService ?? new BinanceService(settings, logger);
        }

        public async Task Handle(PaymentReceivedEvent notification, CancellationToken cancellationToken)
        {
            _logger.LogInformation($"Notification {notification.GetType().Name} received with payment id #{notification.PaymentId} and event id #{notification.EventId}");
            try
            {
                var payment = await _paymentService.GetByIdAsync(notification.PaymentId);
                if (payment == null)
                {
                    _logger.LogWarning($"Payment not found: {notification.PaymentId}");
                    return;
                }

                // Define Polly retry policy
                var policy = Policy
                    .Handle<Exception>()
                    .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                        onRetry: (ex, time, retryCount, context) =>
                            _logger.LogWarning("Retry {RetryCount} for PaymentId: {PaymentId} due to {Exception}",
                                retryCount, notification.PaymentId, ex.Message));

                // Execute with retries
                await policy.ExecuteAsync(async () =>
                {
                    var result = await ProcessPayment(payment);
                    if (!result.IsSuccess)
                    {
                        throw new Exception($"Order processing failed: {string.Join(", ", result.Orders.Where(o => !o.IsSuccess).Select(o => $"Asset id #{o.CoinId}: {o.ErrorMessage}"))}");
                    }
                });

                var eventData = await _eventService.GetByIdAsync(notification.EventId);
                // Mark event as processed
                await _eventService.UpdateOneAsync(eventData._id, new
                {
                    Processed = true,
                    ProcessedAt = DateTime.UtcNow
                });

                _logger.LogInformation("Successfully processed PaymentId: {PaymentId}", notification.PaymentId);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to process PaymentReceivedEvent for PaymentId: {notification.PaymentId} after retries: {ex.Message}");
                // Event remains unprocessed in MongoDB for recovery
            }
        }

        public async Task<ResultWrapper<PlacedExchangeOrder>> PlaceExchangeOrderAsync(AssetData coin, decimal quantity, ObjectId subcriptionId, string side = Domain.Constants.OrderSide.Buy, string type = "MARKET")
        {
            try
            {
                if (quantity <= 0m)
                {
                    throw new ArgumentOutOfRangeException(nameof(quantity), $"Quantity must be greater than zero. Provided value is {quantity}");
                }

                if (string.IsNullOrEmpty(coin.Ticker))
                {
                    throw new ArgumentException("Coin ticker cannot be null or empty.", nameof(coin));
                }

                BinancePlacedOrder order = new();
                var symbol = coin.Ticker + "USDT";

                if (side == Domain.Constants.OrderSide.Buy)
                {
                    //Returns BinancePlacedOrder, otherwise throws Exception
                    order = await _binanceService.PlaceSpotMarketBuyOrder(symbol, quantity, subcriptionId);
                }
                else if (side == Domain.Constants.OrderSide.Sell)
                {
                    //Returns BinancePlacedOrder, otherwise throws Exception
                    order = await _binanceService.PlaceSpotMarketSellOrder(symbol, quantity, subcriptionId);
                }
                else
                {
                    throw new ArgumentException("Invalid order side. Allowed values are BUY or SELL.", nameof(side));
                }

                var placedOrder = new PlacedExchangeOrder()
                {
                    CryptoId = coin._id,
                    QuoteQuantity = order.QuoteQuantity,
                    QuoteQuantityFilled = order.QuoteQuantityFilled,
                    Price = order.AverageFillPrice,
                    QuantityFilled = order.QuantityFilled,
                    OrderId = order.Id,
                    Status = MapBinanceStatus(order.Status),
                };

                if (placedOrder.Status == OrderStatus.Failed)
                {
                    return ResultWrapper<PlacedExchangeOrder>.Failure(FailureReason.ExchangeApiError, $"Order failed with status {order.Status}");
                }

                _logger.LogInformation("Order created successfully for symbol: {Symbol}, OrderId: {OrderId}, Status: {Status}", symbol, order.Id, order.Status);
                return ResultWrapper<PlacedExchangeOrder>.Success(placedOrder);
            }
            catch (Exception ex)
            {
                string reason = ex switch
                {
                    ArgumentOutOfRangeException => FailureReason.ValidationError,
                    ArgumentException => FailureReason.ValidationError,
                    KeyNotFoundException => FailureReason.DataNotFound,
                    MongoException => FailureReason.DatabaseError,
                    _ when ex.Message.Contains("Binance") => FailureReason.ExchangeApiError,
                    _ when ex.Message.Contains("insert") => FailureReason.DatabaseError,
                    _ => FailureReason.Unknown
                };
                _logger.LogError(ex, "Failed to create order: {Message}", ex.Message);
                return ResultWrapper<PlacedExchangeOrder>.Failure(reason, $"{ex.Message}");
            }
        }

        public async Task<AllocationOrdersResult> ProcessPayment(PaymentData payment)
        {
            var orderResults = new List<OrderResult>();
            var netAmount = payment.NetAmount;

            if (netAmount <= 0m)
            {
                orderResults.Add(OrderResult.Failure(null, FailureReason.ValidationError, $"Invalid transaction net amount. Must be greater than zero. Provided value is {netAmount}"));
                return new AllocationOrdersResult(orderResults.AsReadOnly());
            }

            // Check exchange fiat balance
            if (!await CheckExchangeBalanceAsync(netAmount))
            {
                _logger.LogWarning("Insufficient fiat balance for {Required}", netAmount);
                orderResults.Add(OrderResult.Failure(null, FailureReason.InsufficientBalance, $"Insufficient exchange balance for {netAmount}"));
                //RequestFunding();
                return new AllocationOrdersResult(orderResults.AsReadOnly());
            }

            var fetchAllocationsResult = await _subscriptionService.GetAllocationsAsync(payment.SubscriptionId);
            if (!fetchAllocationsResult.IsSuccess || fetchAllocationsResult.Data is null || !fetchAllocationsResult.Data.Any())
            {
                orderResults.Add(OrderResult.Failure(null, FailureReason.ValidationError, $"Unable to fetch coin allocations: {fetchAllocationsResult.ErrorMessage}"));
                return new AllocationOrdersResult(orderResults.AsReadOnly());
            }


            foreach (var alloc in fetchAllocationsResult.Data)
            {
                try
                {
                    if (alloc.PercentAmount <= 0 || alloc.PercentAmount > 100)
                        throw new ArgumentOutOfRangeException(nameof(alloc.PercentAmount), "Allocation must be between 0-100.");

                    decimal quoteOrderQuantity = netAmount * (alloc.PercentAmount / 100m);
                    if (quoteOrderQuantity <= 0)
                        throw new ArgumentException($"Quote order quantity must be positive. Value: {quoteOrderQuantity}");

                    //First check if there are previous orders already processed with this payment.

                    var filter = new FilterDefinitionBuilder<ExchangeOrderData>()
                        .Where(o => o.TransactionId == payment._id && o.CryptoId == alloc.AssetId && (o.Status == OrderStatus.Filled || o.Status == OrderStatus.PartiallyFilled));

                    var previousFilledOrders = await GetAllAsync(filter);
                    decimal previousOrdersSum = previousFilledOrders?.Sum(o => o.QuoteQuantityFilled) ?? 0m;
                    var remainingQuoteOrderQuantity = quoteOrderQuantity - previousOrdersSum;

                    var coinData = await _assetService.GetByIdAsync(alloc.AssetId);
                    if (coinData is null)
                        throw new KeyNotFoundException($"Unable to fetch coin #{alloc.AssetId} data.");

                    var symbol = coinData.Ticker + "USDT";
                    var placedOrder = await PlaceExchangeOrderAsync(coinData, remainingQuoteOrderQuantity, payment.SubscriptionId);
                    if (!placedOrder.IsSuccess || placedOrder.Data?.Status == OrderStatus.Failed)
                        throw new Exception($"Unable to place order. Reason: {placedOrder.ErrorMessage}");
                    // Atomic insert of transaction and event
                    using (var session = await _mongoClient.StartSessionAsync())
                    {
                        session.StartTransaction();
                        try
                        {
                            var insertOrderData = new ExchangeOrderData()
                            {
                                UserId = payment.UserId,
                                TransactionId = payment._id,
                                OrderId = placedOrder.Data?.OrderId,
                                CryptoId = placedOrder.Data.CryptoId,
                                QuoteQuantity = placedOrder.Data.QuoteQuantity,
                                QuoteQuantityFilled = placedOrder.Data.QuoteQuantityFilled,
                                Quantity = placedOrder.Data.QuantityFilled,
                                Price = placedOrder.Data.Price,
                                Status = placedOrder.Data.Status
                            };

                            var insertResult = await InsertOneAsync(session, insertOrderData);
                            if (!insertResult.IsAcknowledged)
                            {
                                //TO-DO: Fallback save ExchangeOrderData locally to reconcile later.
                                _logger.LogError($"Failed to create order record: {insertResult?.ErrorMessage}");
                                throw new MongoException($"Failed to create order record: {insertResult?.ErrorMessage}");
                            }

                            BalanceData updateBalance = new BalanceData()
                            {
                                UserId = payment.UserId,
                                SubscriptionId = payment.SubscriptionId,
                                AssetId = placedOrder.Data.CryptoId,
                                Available = placedOrder.Data.QuantityFilled
                            };
                            var updateBalanceResult = await _balanceService.UpsertBalanceAsync(session, insertOrderData._id, payment.SubscriptionId, updateBalance);
                            if (!updateBalanceResult.IsSuccess)
                            {
                                //TO-DO: Fallback save update BalanceData locally to reconcile later.
                                _logger.LogError($"Failed to update balances: {updateBalanceResult.ErrorMessage}");
                                throw new MongoException($"Failed to update balances: {updateBalanceResult?.ErrorMessage}");
                            }

                            await session.CommitTransactionAsync();

                            orderResults.Add(OrderResult.Success(
                                placedOrder.Data.OrderId,
                                true,
                                true,
                                coinData._id.ToString(),
                                placedOrder.Data.QuoteQuantity,
                                placedOrder.Data.QuantityFilled,
                                placedOrder.Data.Status
                                ));
                        }
                        catch (Exception ex)
                        {
                            await session.AbortTransactionAsync();
                            _logger.LogError(ex, "Failed to atomically insert transaction and event");
                            orderResults.Add(OrderResult.Success(
                                placedOrder.Data.OrderId,
                                false,
                                false,
                                coinData._id.ToString(),
                                placedOrder.Data.QuoteQuantity,
                                placedOrder.Data.QuantityFilled,
                                placedOrder.Data.Status
                                ));
                        }
                    }

                    _logger.LogInformation("Order created for {Symbol}, OrderId: {OrderId}", symbol, placedOrder.Data.OrderId);
                }
                catch (Exception ex)
                {
                    string reason = ex switch
                    {
                        ArgumentOutOfRangeException => FailureReason.ValidationError,
                        ArgumentException => FailureReason.ValidationError,
                        KeyNotFoundException => FailureReason.DataNotFound,
                        MongoException => FailureReason.DatabaseError,
                        _ when ex.Message.Contains("Binance") => FailureReason.ExchangeApiError,
                        _ when ex.Message.Contains("insert") => FailureReason.DatabaseError,
                        _ when ex.Message.Contains("update") => FailureReason.DatabaseError,
                        _ => FailureReason.Unknown
                    };
                    orderResults.Add(OrderResult.Failure(alloc?.AssetId.ToString(), reason, ex.Message));
                    _logger.LogError(ex, "Failed to process order: {Message}", ex.Message);
                }
            }
            return new AllocationOrdersResult(orderResults.AsReadOnly());
        }

        public async Task<bool> CheckExchangeBalanceAsync(decimal amount)
        {
            // Check exchange fiat balance
            decimal fiatBalance = await _binanceService.GetFiatBalanceAsync("USDT");
            return (fiatBalance >= amount);
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
                    if (order.OrderId is null) throw new ArgumentNullException(nameof(order.OrderId));
                    BinancePlacedOrder exchangeOrder = await _binanceService.GetOrderInfoAsync((long)order.OrderId); // TO-DO: Create GetOrderStatus dunction

                    if (exchangeOrder is null) throw new ArgumentNullException(nameof(exchangeOrder));

                    var update = Builders<ExchangeOrderData>.Update
                        .Set(o => o.Status, exchangeOrder.Status.ToString()) // Map Binance status to your enum/string
                        .Set(o => o.Quantity, exchangeOrder.QuantityFilled)
                        .Set(o => o.Price, exchangeOrder.Price);

                    await UpdateOneAsync(order._id, new
                    {
                        Status = exchangeOrder.Status.ToString(),
                        Quantity = exchangeOrder.QuantityFilled,
                        Price = exchangeOrder.Price
                    });

                    if (exchangeOrder.Status == Binance.Net.Enums.OrderStatus.Rejected || exchangeOrder.Status == Binance.Net.Enums.OrderStatus.Canceled || exchangeOrder.Status == Binance.Net.Enums.OrderStatus.Expired)
                        await HandleFailedOrderAsync(order);
                    else if (exchangeOrder.Status == Binance.Net.Enums.OrderStatus.PartiallyFilled)
                        await HandlePartiallyFilledOrderAsync(order);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to reconcile order {OrderId}: {Message}", order.OrderId, ex.Message);
                }
            }
        }

        private async Task HandleFailedOrderAsync(ExchangeOrderData order)
        {
            if (order.RetryCount >= 3)
            {
                _logger.LogError("Max retries reached for OrderId: {OrderId}", order.OrderId);
                await UpdateOneAsync(order._id, new
                {
                    Status = OrderStatus.Failed
                });
                return;
            }

            var retryOrder = new ExchangeOrderData
            {
                UserId = order.UserId,
                TransactionId = order.TransactionId,
                CryptoId = order.CryptoId,
                QuoteQuantity = order.QuoteQuantity,
                Status = OrderStatus.Queued,
                PreviousOrderId = order._id,
                RetryCount = order.RetryCount + 1
            };
            await EnqueuOrderAsync(retryOrder);
            _logger.LogInformation("Queued retry order for failed OrderId: {OrderId}", order.OrderId);
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
                    TransactionId = order.TransactionId,
                    CryptoId = order.CryptoId,
                    QuoteQuantity = remainingQty,
                    Status = OrderStatus.Queued,
                    RetryCount = order.RetryCount + 1,
                    PreviousOrderId = order._id
                };
                await EnqueuOrderAsync(retryOrder); //TO-DO: Create EnqueueOrderAsync function
                _logger.LogInformation("Queued partial fill order for OrderId: {OrderId}, RemainingQty: {Remaining}", order.OrderId, remainingQty);
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
                            assetResult.Data,
                            balance.Available,
                            ObjectId.Empty, // No subscription ID needed for reset
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
                        var orderData = new ExchangeOrderData
                        {
                            UserId = ObjectId.Empty, // System-initiated reset, no user
                            TransactionId = ObjectId.Empty, // No specific transaction
                            OrderId = order.OrderId,
                            CryptoId = assetResult.Data._id,
                            QuoteQuantity = order.QuoteQuantity,
                            QuoteQuantityFilled = order.QuoteQuantityFilled,
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
                            true,
                            true,
                            assetResult.Data._id.ToString(),
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