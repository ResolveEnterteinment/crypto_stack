using Application.Interfaces;
using Application.Interfaces.Asset;
using Application.Interfaces.Exchange;
using Application.Interfaces.Logging;
using Application.Interfaces.Payment;
using Application.Interfaces.Subscription;
using Domain.Constants;
using Domain.DTOs;
using Domain.DTOs.Exchange;
using Domain.Events;
using Domain.Exceptions;
using Domain.Models.Balance;
using Domain.Models.Exchange;
using Domain.Models.Payment;
using Domain.Models.Transaction;
using MediatR;
using MongoDB.Driver;
using Polly;
using System.Diagnostics;

namespace Infrastructure.Services.Exchange
{
    /// <summary>
    /// Service responsible for processing payments and executing exchange orders
    /// </summary>
    public class PaymentProcessingService : IPaymentProcessingService, INotificationHandler<PaymentReceivedEvent>
    {
        private readonly ISubscriptionService _subscriptionService;
        private readonly IAssetService _assetService;
        private readonly IBalanceService _balanceService;
        private readonly IExchangeService _exchangeService;
        private readonly IPaymentService _paymentService;
        private readonly IBalanceManagementService _balanceManagementService;
        private readonly ITransactionService _transactionService;
        private readonly IOrderManagementService _orderManagementService;
        private readonly IEventService _eventService;
        private readonly IIdempotencyService _idempotencyService;
        private readonly ILoggingService _logger;

        public PaymentProcessingService(
            ISubscriptionService subscriptionService,
            IAssetService assetService,
            IBalanceService balanceService,
            ITransactionService transactionService,
            IExchangeService exchangeService,
            IPaymentService paymentService,
            IBalanceManagementService balanceManagementService,
            IOrderManagementService orderManagementService,
            IEventService eventService,
            IIdempotencyService idempotencyService,
            ILoggingService logger)
        {
            _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
            _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
            _balanceService = balanceService ?? throw new ArgumentNullException(nameof(balanceService));
            _transactionService = transactionService ?? throw new ArgumentNullException(nameof(transactionService));
            _exchangeService = exchangeService ?? throw new ArgumentNullException(nameof(exchangeService));
            _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
            _balanceManagementService = balanceManagementService ?? throw new ArgumentNullException(nameof(balanceManagementService));
            _orderManagementService = orderManagementService ?? throw new ArgumentNullException(nameof(orderManagementService));
            _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
            _idempotencyService = idempotencyService ?? throw new ArgumentNullException(nameof(idempotencyService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Handles payment received events to process crypto purchases
        /// </summary>
        public async Task Handle(PaymentReceivedEvent notification, CancellationToken cancellationToken)
        {
            var correlationId = notification.EventId.ToString();
            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["CorrelationId"] = correlationId,
                ["PaymentId"] = notification.Payment.Id,
                ["UserId"] = notification.Payment.UserId
            });

            try
            {
                // Check for idempotency to avoid double processing
                string idempotencyKey = $"payment_event_{notification.EventId}";
                var (exists, result) = await _idempotencyService.GetResultAsync<bool>(idempotencyKey);

                if (exists)
                {
                    _logger.LogWarning("Payment event {EventId} already processed", notification.EventId);
                    return;
                }

                // Define a retry policy for payment processing
                var retryPolicy = Policy
                    .Handle<Exception>(ex => !(ex is ValidationException)) // Don't retry validation errors
                    .WaitAndRetryAsync(
                        3,
                        retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                        onRetry: (ex, retryCount, context) =>
                        {
                            _logger.LogWarning("Retry {RetryCount}/3 processing payment {PaymentId}",
                                retryCount, notification.Payment.Id);
                        });

                // Execute with retries
                await retryPolicy.ExecuteAsync(async () =>
                {
                    var result = await ProcessPayment(notification.Payment);
                    if (!result.IsSuccess)
                    {
                        var failedOrders = result.Data?.Where(o => !o.IsSuccess).ToList() ?? new List<OrderResult>();
                        if (failedOrders.Any())
                        {
                            string errorDetails = string.Join(", ", failedOrders.Select(o =>
                                $"Asset {o.AssetId}: {o.ErrorMessage}"));

                            throw new ExchangeApiException(
                                $"Failed to process orders: {errorDetails}",
                                notification.Payment.Provider);
                        }
                    }
                });

                // Mark event as processed
                await _eventService.UpdateAsync(notification.EventId, new
                {
                    Processed = true,
                    ProcessedAt = DateTime.UtcNow
                }, cancellationToken);

                // Store record for idempotency
                await _idempotencyService.StoreResultAsync(idempotencyKey, true);

                _logger.LogInformation("Successfully processed payment: {PaymentId}", notification.Payment.Id);
            }
            catch (Exception ex)
            {
                await _logger.LogTraceAsync($"Failed to process PaymentReceivedEvent for payment ID {notification.Payment.Id}: {ex.Message}",
                    level: Domain.Constants.Logging.LogLevel.Error,
                    requiresResolution: true);

                // Mark event as failed but not processed so it can be retried
                await _eventService.UpdateAsync(notification.EventId, new
                {
                    ErrorMessage = ex.Message,
                    LastAttempt = DateTime.UtcNow
                });

                // For critical errors, we may want to trigger an alert
                if (ex is DatabaseException || ex is ExchangeApiException)
                {
                    // TODO: Add alerting mechanism for critical failures
                    await _logger.LogTraceAsync($"Critical failure processing payment ID {notification.Payment.Id}: {ex.Message}",
                    level: Domain.Constants.Logging.LogLevel.Error,
                    requiresResolution: true);
                }
            }
        }

        /// <summary>
        /// Processes a payment by executing crypto purchase orders based on subscription allocations
        /// </summary>
        public async Task<ResultWrapper<IEnumerable<OrderResult>>> ProcessPayment(PaymentData payment)
        {
            var activityId = Activity.Current?.Id ?? Guid.NewGuid().ToString();
            using var scope = _logger.BeginScope(new
            {
                PaymentId = payment.Id,
                Amount = payment.NetAmount,
                Currency = payment.Currency
            });

            var orderResults = new List<OrderResult>();
            var netAmount = payment.NetAmount;

            try
            {
                // Check for idempotency to avoid double processing
                string idempotencyKey = $"payment_id_{payment.Id}";
                var (exists, result) = await _idempotencyService.GetResultAsync<bool>(idempotencyKey);

                if (exists)
                {
                    _logger.LogWarning("Payment id {PaymentId} already processed", payment.Id);
                    return ResultWrapper<IEnumerable<OrderResult>>.Failure(FailureReason.IdempotencyConflict, $"Payment id {payment.Id} already processed");
                }

                _logger.LogInformation("Starting payment processing for {PaymentId} with amount {Amount} {Currency}",
                    payment.Id, netAmount, payment.Currency);

                // Basic validation
                if (payment == null)
                {
                    throw new ArgumentNullException(nameof(payment), "Payment data cannot be null");
                }

                if (netAmount <= 0m)
                {
                    throw new ValidationException("Invalid payment amount",
                        new Dictionary<string, string[]>
                        {
                            ["NetAmount"] = new[] { $"Amount must be greater than zero. Received: {netAmount}" }
                        });
                }

                // Fetch allocations for this subscription
                var fetchAllocationsResult = await _subscriptionService.GetAllocationsAsync(payment.SubscriptionId);
                if (!fetchAllocationsResult.IsSuccess || fetchAllocationsResult.Data is null || !fetchAllocationsResult.Data.Any())
                {
                    var error = fetchAllocationsResult.ErrorMessage ?? "No allocations found";
                    _logger.LogError("Failed to fetch allocations for subscription {SubscriptionId}: {Error}",
                        payment.SubscriptionId, error);

                    orderResults.Add(OrderResult.Failure(null, FailureReason.ValidationError,
                        $"Unable to fetch asset allocations: {error}"));

                    return ResultWrapper<IEnumerable<OrderResult>>.Failure(
                        FailureReason.ValidationError,
                        $"Unable to fetch asset allocations: {error}");
                }

                _logger.LogInformation("Processing {Count} allocations for subscription {SubscriptionId}",
                    fetchAllocationsResult.Data.Count(), payment.SubscriptionId);

                // Process each allocation
                foreach (var alloc in fetchAllocationsResult.Data)
                {
                    using var allocationScope = _logger.BeginScope(new
                    {
                        Allocation = alloc,
                    });

                    string assetId = alloc.AssetId.ToString();
                    try
                    {
                        // Validate allocation percentage
                        if (alloc.PercentAmount <= 0 || alloc.PercentAmount > 100)
                        {
                            throw new ValidationException("Invalid allocation percentage",
                                new Dictionary<string, string[]>
                                {
                                    ["PercentAmount"] = new[] {
                                        $"Allocation must be between 0-100, received: {alloc.PercentAmount}"
                                    }
                                });
                        }

                        // Calculate order amount based on allocation percentage
                        decimal quoteOrderQuantity = netAmount * (alloc.PercentAmount / 100m);
                        if (quoteOrderQuantity <= 0m)
                        {
                            throw new ValidationException("Invalid order quantity",
                                new Dictionary<string, string[]>
                                {
                                    ["QuoteOrderQuantity"] = new[] {
                                        $"Order quantity must be positive, calculated: {quoteOrderQuantity}"
                                    }
                                });
                        }

                        // Get asset details
                        var assetResult = await _assetService.GetByIdAsync(alloc.AssetId);
                        if (assetResult == null || !assetResult.IsSuccess)
                        {
                            throw new ResourceNotFoundException("Asset", alloc.AssetId.ToString());
                        }

                        var asset = assetResult.Data;

                        // Get the exchange for this asset
                        if (string.IsNullOrEmpty(asset.Exchange) ||
                            !_exchangeService.Exchanges.TryGetValue(asset.Exchange, out var exchange))
                        {
                            throw new ValidationException("Invalid exchange",
                                new Dictionary<string, string[]>
                                {
                                    ["Exchange"] = new[] { $"No exchange configured for asset {asset.Ticker}" }
                                });
                        }

                        var reserveAssetTicker = exchange.ReserveAssetTicker;

                        // Check exchange balance to ensure sufficient funds
                        var checkBalanceResult = await _balanceManagementService.CheckExchangeBalanceAsync(
                            asset.Exchange, reserveAssetTicker, quoteOrderQuantity);

                        if (!checkBalanceResult.IsSuccess)
                        {
                            throw new Exception($"Failed to check exchange balance: {checkBalanceResult.ErrorMessage}");
                        }

                        if (checkBalanceResult.Data is false)
                        {
                            throw new InsufficientBalanceException(checkBalanceResult.DataMessage ??
                                $"Insufficient balance in {exchange.Name} to process order");
                        }

                        // Check for previous filled orders to avoid duplication
                        var previousFilledSumResult = await _orderManagementService.GetPreviousOrdersSum(
                            exchange, asset, payment);

                        if (!previousFilledSumResult.IsSuccess)
                        {
                            throw new OrderFetchException(
                                $"Failed to fetch previous orders for {payment.PaymentProviderId}");
                        }

                        // Calculate remaining amount after accounting for previously filled orders
                        var remainingQuoteOrderQuantity = quoteOrderQuantity - previousFilledSumResult.Data;

                        var minNotionalResult = await _orderManagementService.GetMinNotional(exchange, asset);
                        if (minNotionalResult == null|| !minNotionalResult.IsSuccess)
                        {

                            //Do not throw here, just log the error and continue. Exchnage will handle it.
                            await _logger.LogTraceAsync($"Failed to fetch minimum notional for {asset.Ticker} on {exchange.Name}: {minNotionalResult?.ErrorMessage ?? "Unknown error"}",
                                level: Domain.Constants.Logging.LogLevel.Error);
                        }

                        var minNotional = minNotionalResult?.Data ?? 0m;

                        // Skip if already fully filled (idempotency check)
                        if (remainingQuoteOrderQuantity <= minNotional)
                        {
                            _logger.LogInformation("Order for asset {AssetId} already fully processed or remaining quantity is less than minimum notional value.",
                                alloc.AssetId);

                            orderResults.Add(OrderResult.Success(
                                0, // No new order placed
                                assetId,
                                quoteOrderQuantity,
                                0,
                                "ALREADY_PROCESSED"));

                            continue;
                        }

                        _logger.LogInformation("Placing order for {Ticker}: {Amount} {Currency} ({Percent}%)",
                            asset.Ticker, remainingQuoteOrderQuantity, reserveAssetTicker, alloc.PercentAmount);

                        // Place the order
                        var placedOrderResult = await _orderManagementService.PlaceExchangeOrderAsync(
                            exchange, asset.Ticker, remainingQuoteOrderQuantity, payment.PaymentProviderId);

                        if (!placedOrderResult.IsSuccess || placedOrderResult.Data is null)
                        {
                            throw new OrderExecutionException(
                                $"Unable to place order: {placedOrderResult.ErrorMessage}",
                                exchange.Name);
                        }

                        var placedOrder = placedOrderResult.Data;
                        if (placedOrder.Status == OrderStatus.Failed)
                        {
                            throw new OrderExecutionException(
                                $"Order failed with status: {placedOrder.Status}",
                                exchange.Name,
                                placedOrder.OrderId.ToString());
                        }

                        // Handle dust quantity (small leftover amounts)
                        await _orderManagementService.HandleDustAsync(placedOrder);

                        // Execute database operations in a transaction for ACID compliance
                        await _exchangeService.ExecuteInTransactionAsync(async (session) =>
                        {
                            // Record the exchange order
                            var exchangeOrder = new ExchangeOrderData
                            {
                                UserId = payment.UserId,
                                PaymentProviderId = payment.PaymentProviderId,
                                SubscriptionId = payment.SubscriptionId,
                                PlacedOrderId = placedOrder.OrderId,
                                AssetId = alloc.AssetId,
                                Exchange = exchange.Name,
                                QuoteQuantity = placedOrder.QuoteQuantity,
                                QuoteQuantityFilled = placedOrder.QuoteQuantityFilled,
                                Quantity = placedOrder.QuantityFilled,
                                Price = placedOrder.Price,
                                Status = placedOrder.Status
                            };

                            var insertOrderResult = await _exchangeService.InsertAsync(exchangeOrder);
                            if (insertOrderResult == null || !insertOrderResult.IsSuccess)
                            {
                                throw new DatabaseException(
                                    $"Failed to create order record: {insertOrderResult?.ErrorMessage ?? "Unknown error"}");
                            }

                            // Update user's balance
                            var balanceUpdate = new BalanceData
                            {
                                UserId = payment.UserId,
                                AssetId = alloc.AssetId,
                                Ticker = alloc.AssetTicker,
                                Available = placedOrder.QuantityFilled,
                                LastUpdated = DateTime.UtcNow
                            };

                            var updateBalanceResult = await _balanceService.UpsertBalanceAsync(
                                payment.UserId, balanceUpdate, session);

                            if (updateBalanceResult is null || !updateBalanceResult.IsSuccess || updateBalanceResult.Data is null)
                            {
                                throw new DatabaseException(
                                    $"Failed to update balances: {updateBalanceResult?.ErrorMessage ?? "Unknown error"}");
                            }

                            // Record the transaction
                            var transaction = new TransactionData
                            {
                                UserId = payment.UserId,
                                PaymentProviderId = payment.PaymentProviderId,
                                SubscriptionId = payment.SubscriptionId,
                                BalanceId = updateBalanceResult.Data.Id,
                                SourceName = exchange.Name,
                                SourceId = placedOrder.OrderId.ToString(),
                                Action = $"Exchange Order: Buy",
                                Quantity = placedOrder.QuantityFilled
                            };

                            var insertTransactionResult = await _transactionService.InsertAsync(transaction);
                            if (insertTransactionResult == null || !insertTransactionResult.IsSuccess)
                            {
                                throw new DatabaseException(
                                    $"Failed to create transaction record: {insertTransactionResult?.ErrorMessage ?? "Unknown error"}");
                            }

                            _logger.LogInformation(
                                "Successfully executed order {OrderId} for {Ticker}: {Quantity} @ {Price}",
                                placedOrder.OrderId, asset.Ticker, placedOrder.QuantityFilled, placedOrder.Price);

                            orderResults.Add(OrderResult.Success(
                                placedOrder.OrderId,
                                assetId,
                                placedOrder.QuoteQuantity,
                                placedOrder.QuantityFilled,
                                placedOrder.Status));

                            // Store record for idempotency
                            await _idempotencyService.StoreResultAsync(idempotencyKey, true);
                            return true;
                        });
                    }
                    catch (Exception ex)
                    {
                        var failureReason = FailureReasonExtensions.FromException(ex);
                        orderResults.Add(OrderResult.Failure(assetId, failureReason, ex.Message));
                        await _logger.LogTraceAsync($"Failed to process order for asset {assetId}: {ex.Message}",
                            level: Domain.Constants.Logging.LogLevel.Critical,
                            requiresResolution: true);

                        // Don't rethrow here - we'll continue with other allocations
                    }
                }

                // Check if all orders failed or some succeeded
                if (orderResults.All(r => !r.IsSuccess))
                {
                    return ResultWrapper<IEnumerable<OrderResult>>.Failure(
                        FailureReason.OrderExecutionFailed,
                        "All orders failed to process",
                        null,
                        null,
                        string.Join("; ", orderResults.Select(r => r.ErrorMessage)));
                }

                return ResultWrapper<IEnumerable<OrderResult>>.Success(orderResults);
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to process payment {PaymentId}: {Message}", payment.Id, ex.Message);
                return ResultWrapper<IEnumerable<OrderResult>>.FromException(ex);
            }
        }
    }
}