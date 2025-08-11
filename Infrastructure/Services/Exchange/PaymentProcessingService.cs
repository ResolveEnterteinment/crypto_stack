using Application.Interfaces;
using Application.Interfaces.Asset;
using Application.Interfaces.Base;
using Application.Interfaces.Exchange;
using Application.Interfaces.Logging;
using Application.Interfaces.Payment;
using Application.Interfaces.Subscription;
using Domain.Constants;
using Domain.Constants.Logging;
using Domain.DTOs;
using Domain.DTOs.Exchange;
using Domain.DTOs.Logging;
using Domain.DTOs.Subscription;
using Domain.Events;
using Domain.Events.Payment;
using Domain.Exceptions;
using Domain.Models.Asset;
using Domain.Models.Exchange;
using Domain.Models.Payment;
using MediatR;
using MongoDB.Driver;
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
        private readonly IExchangeService _exchangeService;
        private readonly IBalanceManagementService _balanceManagementService;
        private readonly IOrderManagementService _orderManagementService;
        private readonly IEventService _eventService;
        private readonly IIdempotencyService _idempotencyService;
        private readonly IResilienceService<OrderResult> _resilienceService;
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
            IResilienceService<OrderResult> resilienceService,
            ILoggingService logger)
        {
            _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
            _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
            _exchangeService = exchangeService ?? throw new ArgumentNullException(nameof(exchangeService));
            _balanceManagementService = balanceManagementService ?? throw new ArgumentNullException(nameof(balanceManagementService));
            _orderManagementService = orderManagementService ?? throw new ArgumentNullException(nameof(orderManagementService));
            _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
            _idempotencyService = idempotencyService ?? throw new ArgumentNullException(nameof(idempotencyService));
            _resilienceService = resilienceService ?? throw new ArgumentNullException(nameof(resilienceService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Handles payment received events to process crypto purchases
        /// </summary>
        public async Task Handle(PaymentReceivedEvent notification, CancellationToken cancellationToken)
        {
            string idempotencyKey = $"payment_event_{notification.EventId}";

            await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Exchange",
                    FileName = "PaymentProcessingService",
                    OperationName = "Handle(PaymentReceivedEvent notification, CancellationToken cancellationToken)",
                    State = new()
                    {
                        ["EventName"] = notification.GetType().Name,
                        ["PaymentId"] = notification.Payment.Id,
                        ["SubscriptionId"] = notification.Payment.SubscriptionId,
                        ["UserId"] = notification.Payment.UserId
                    },
                    LogLevel = LogLevel.Critical
                },
                async () =>
                {
                    // Check for idempotency to avoid double processing
                    var (exists, result) = await _idempotencyService.GetResultAsync<bool>(idempotencyKey);

                    if (exists)
                    {
                        _logger.LogWarning("Payment event {EventId} already processed", notification.EventId);
                        return Task.CompletedTask;
                    }

                    //BalanceService already handles user fiat balance and record transaction
                    var ordersResult = await ProcessPayment(notification.Payment);

                    if (!ordersResult.IsSuccess)
                    {
                        var failedOrders = ordersResult.Data?.Where(o => !o.IsSuccess).ToList() ?? new List<OrderResult>();
                        if (failedOrders.Any())
                        {
                            string errorDetails = string.Join(", ", failedOrders.Select(o =>
                                $"Asset {o.AssetId}: {o.ErrorMessage}"));

                            throw new ExchangeApiException(
                                $"Failed to process orders: {errorDetails}",
                                notification.Payment.Provider);
                        }
                    }

                    // Store record for idempotency
                    await _idempotencyService.StoreResultAsync(idempotencyKey, true);

                    _logger.LogInformation("Successfully processed payment: {PaymentId}", notification.Payment.Id);

                    return Task.CompletedTask;
                })
                .WithExchangeOperationResilience()
                .WithPerformanceMonitoring(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30))
                .OnSuccess(async (Task task) =>
                {
                    await _idempotencyService.StoreResultAsync(idempotencyKey, true);
                    await _eventService.MarkAsProcessedAsync(notification.EventId);
                })
                .OnError(async (Exception ex) =>
                {
                    await _eventService.MarkAsFailedAsync(notification.EventId, ex.Message);
                })
                .ExecuteAsync();
        }

        /// <summary>
        /// Processes a payment by executing crypto purchase orders based on subscription allocations
        /// </summary>
        public async Task<ResultWrapper<IEnumerable<OrderResult>>> ProcessPayment(PaymentData payment)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Exchange",
                    FileName = "PaymentProcessingService",
                    OperationName = "ProcessPayment(PaymentData payment)",
                    State = new()
                    {
                        ["PaymentId"] = payment.Id,
                        ["Amount"] = payment.NetAmount,
                        ["Currency"] = payment.Currency,
                        ["SubscriptionId"] = payment.SubscriptionId,
                        ["UserId"] = payment.UserId
                    },
                    LogLevel = LogLevel.Critical
                },
                async () =>
                {
                    var activityId = Activity.Current?.Id ?? Guid.NewGuid().ToString();
                    using var scope = _logger.BeginScope(new
                    {
                        PaymentId = payment.Id,
                        Amount = payment.NetAmount,
                        Currency = payment.Currency
                    });

                    // 1. Validate and prepare payment
                    var validationResult = await ValidateAndPreparePayment(payment);
                    if (!validationResult.IsSuccess)
                        throw new ValidationException(validationResult.ErrorMessage, validationResult.ValidationErrors ?? new Dictionary<string, string[]>());

                    var context = validationResult.Data;

                    // 2. Fetch and validate allocations
                    var allocationsResult = await FetchAndValidateAllocations(payment.SubscriptionId);
                    if (!allocationsResult.IsSuccess)
                        throw new ValidationException(allocationsResult.ErrorMessage ?? "Failed to fetch allocations", allocationsResult.ValidationErrors ?? []);

                    // 3. Process each allocation
                    var orderResultsWr = await ProcessAllocations(payment, allocationsResult.Data, context);
                    if (orderResultsWr == null || !orderResultsWr.IsSuccess)
                        throw new OrderExecutionException($"Failed to process allocations for payment {orderResultsWr?.ErrorMessage ?? "Order results returned null"}" , "N/A");

                    // 4. Evaluate final results
                    var finalResult = EvaluateFinalResults(orderResultsWr.Data);
                    if (!finalResult.IsSuccess)
                        throw new OrderExecutionException(finalResult.ErrorMessage ?? "Order processing failed", "N/A");

                    return finalResult.Data;
                })
                .WithExchangeOperationResilience()
                .WithPerformanceMonitoring(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(45))
                .WithContext("OperationType", "PaymentProcessing")
                .OnSuccess(async (results) =>
                {
                    _logger.LogInformation("Successfully processed payment {PaymentId} with {OrderCount} orders",
                        payment.Id, results?.Count() ?? 0);
                })
                .OnError(async (ex) =>
                {
                    await _logger.LogTraceAsync($"Failed to process payment {payment.Id}: {ex.Message}",
                        level: LogLevel.Critical, requiresResolution: true);
                })
                .ExecuteAsync();
        }

        #region Private Helper Methods

        /// <summary>
        /// Validates payment and performs idempotency checks
        /// </summary>
        private async Task<ResultWrapper<PaymentProcessingContext>> ValidateAndPreparePayment(PaymentData payment)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Exchange",
                    FileName = "PaymentProcessingService",
                    OperationName = "ValidateAndPreparePayment(PaymentData payment)",
                    State = new()
                    {
                        ["PaymentId"] = payment.Id,
                        ["Amount"] = payment.NetAmount,
                        ["Currency"] = payment.Currency,
                        ["SubscriptionId"] = payment.SubscriptionId,
                        ["UserId"] = payment.UserId
                    },
                    LogLevel = LogLevel.Critical
                },
                async () =>
                {
                    // Check for idempotency to avoid double processing
                    string idempotencyKey = $"payment_id_{payment.Id}";
                    var (exists, result) = await _idempotencyService.GetResultAsync<bool>(idempotencyKey);

                    if (exists)
                    {
                        _logger.LogWarning("Payment id {PaymentId} already processed", payment.Id);
                        throw new IdempotencyException(payment.Id.ToString());
                    }

                    _logger.LogInformation("Starting payment processing for {PaymentId} with amount {Amount} {Currency}",
                        payment.Id, payment.NetAmount, payment.Currency);

                    // Basic validation
                    if (payment == null)
                    {
                        throw new ValidationException("Payment data cannot be null",
                            new Dictionary<string, string[]>
                            {
                                [nameof(payment)] = new[] { "Payment data cannot be null" }
                            });
                    }

                    if (payment.NetAmount <= 0m)
                    {
                        throw new ValidationException(
                            "Payment amount must be greater than zero",
                            new Dictionary<string, string[]>
                            {
                                ["NetAmount"] = new[] { $"Amount must be greater than zero. Received: {payment.NetAmount}" }
                            });
                    }

                    return new PaymentProcessingContext
                    {
                        IdempotencyKey = idempotencyKey,
                        NetAmount = payment.NetAmount
                    };
                })
                .ExecuteAsync();
        }

        /// <summary>
        /// Fetches and validates subscription allocations
        /// </summary>
        private async Task<ResultWrapper<List<AllocationDto>>> FetchAndValidateAllocations(Guid subscriptionId)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Exchange",
                    FileName = "PaymentProcessingService",
                    OperationName = "FetchAndValidateAllocations(Guid subscriptionId)",
                    State = new()
                    {
                        ["SubscriptionId"] = subscriptionId,
                    },
                    LogLevel = LogLevel.Critical
                },
                async () =>
                {
                    var fetchAllocationsResult = await _subscriptionService.GetAllocationsAsync(subscriptionId);

                    if (!fetchAllocationsResult.IsSuccess || fetchAllocationsResult.Data is null || !fetchAllocationsResult.Data.Any())
                    {
                        var error = fetchAllocationsResult.ErrorMessage ?? "No allocations found";
                        throw new SubscriptionFetchException($"Unable to fetch subscription allocations: {error}");
                    }

                    _logger.LogInformation("Processing {Count} allocations for subscription {SubscriptionId}",
                        fetchAllocationsResult.Data.Count(), subscriptionId);

                    return fetchAllocationsResult.Data;
                })
                .ExecuteAsync();
        }

        /// <summary>
        /// Processes all allocations and returns order results
        /// </summary>
        private async Task<ResultWrapper<List<OrderResult>>> ProcessAllocations(
            PaymentData payment,
            IEnumerable<AllocationDto> allocations,
            PaymentProcessingContext context)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Exchange",
                    FileName = "PaymentProcessingService",
                    OperationName = "ProcessAllocations(PaymentData payment, IEnumerable<AllocationDto> allocations, PaymentProcessingContext context)",
                    State = new()
                    {
                        ["PaymentId"] = payment.Id,
                        ["SubscriptionId"] = payment.SubscriptionId,
                        ["UserId"] = payment.UserId,
                        ["AllocationCount"] = allocations.Count(),
                        ["NetAmount"] = context.NetAmount
                    },
                    LogLevel = LogLevel.Critical
                },
                async () =>
                {
                    var orderResults = new List<OrderResult>();
                    var allocationsList = allocations.ToList(); // Convert to list for better enumeration

                    _logger.LogInformation("Starting to process {AllocationCount} allocations for payment {PaymentId}",
                        allocationsList.Count, payment.Id);

                    // Process each allocation with individual resilience handling
                    for (int i = 0; i < allocationsList.Count; i++)
                    {
                        var allocation = allocationsList[i];
                        var allocationIndex = i + 1;

                        try
                        {
                            _logger.LogInformation("Processing allocation {AllocationIndex}/{TotalAllocations} for asset {AssetId}",
                                allocationIndex, allocationsList.Count, allocation.AssetId);

                            // Process single allocation with its own resilience wrapper
                            var processResult = await ProcessSingleAllocation(payment, allocation, context);

                            if (processResult == null || !processResult.IsSuccess)
                                throw new ExchangeApiException($" Failed to process single allocation: {processResult?.ErrorMessage ?? "Process result returned null"}");
                            
                            var order = processResult.Data;
                            orderResults.Add(order);

                            _logger.LogInformation("Successfully processed allocation {AllocationIndex}/{TotalAllocations} for asset {AssetId}: {Status}",
                                allocationIndex, allocationsList.Count, allocation.AssetId, order.Status);
                        }
                        catch (Exception ex)
                        {
                            var failureReason = FailureReasonExtensions.FromException(ex);
                            var exchange = ex is OrderExecutionException oeex ? oeex.Exchange : "N/A";
                            var failedResult = OrderResult.Failure(exchange, allocation.AssetId.ToString(), failureReason, ex.Message);

                            orderResults.Add(failedResult);

                            await _logger.LogTraceAsync(
                                $"Failed to process order for asset {allocation.AssetId}: {ex.Message}",
                                level: LogLevel.Critical,
                                requiresResolution: true);

                            // Don't throw here - we want to continue processing other allocations
                            // The overall result evaluation will happen later
                        }
                    }

                    _logger.LogInformation("Completed processing {AllocationCount} allocations. Success: {SuccessCount}, Failed: {FailedCount}",
                        allocationsList.Count,
                        orderResults.Count(r => r.IsSuccess),
                        orderResults.Count(r => !r.IsSuccess));

                    return orderResults;
                })
                .WithPerformanceMonitoring(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1))
                .WithContext("OperationType", "AllocationProcessing")
                .OnSuccess(async (results) =>
                {
                    var successCount = results.Count(r => r.IsSuccess);
                    var failedCount = results.Count(r => !r.IsSuccess);

                    _logger.LogInformation("Allocation processing completed successfully. Total: {Total}, Success: {Success}, Failed: {Failed}",
                        results.Count, successCount, failedCount);
                })
                .OnError(async (ex) =>
                {
                    await _logger.LogTraceAsync(
                        $"Critical failure in allocation processing for payment {payment.Id}: {ex.Message}",
                        level: LogLevel.Critical,
                        requiresResolution: true);
                })
                .ExecuteAsync();
        }

        /// <summary>
        /// Processes a single allocation
        /// </summary>
        private async Task<ResultWrapper<OrderResult>> ProcessSingleAllocation(
            PaymentData payment,
            AllocationDto allocation,
            PaymentProcessingContext context)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Exchange",
                    FileName = "PaymentProcessingService",
                    OperationName = "ProcessSingleAllocation(PaymentData payment, AllocationDto allocation, PaymentProcessingContext context)",
                    State = new()
                    {
                        ["UserId"] = payment.UserId,
                        ["PaymentId"] = payment.Id,
                        ["AssetId"] = allocation.AssetId,
                        ["AllocationPercent"] = allocation.PercentAmount,
                        ["NetAmount"] = context.NetAmount,
                    },
                    LogLevel = LogLevel.Critical // Individual allocation failures are less critical than overall failure
                },
                async () =>
                {
                    try
                    {
                        string assetId = allocation.AssetId.ToString();

                        // 1. Get and validate asset
                        var asset = await GetAndValidateAssetAsync(allocation.AssetId);

                        // 2. Get and validate exchange
                        var exchange = GetAndValidateExchange(asset);

                        // 3. Validate allocation
                        var validationResult = ValidateAllocation(allocation, context.NetAmount);

                        if (!validationResult.IsSuccess && validationResult.ValidationErrors.Count > 0)
                        {
                            throw new ValidationException(validationResult.ErrorMessage, validationResult.ValidationErrors);
                        }

                        var quoteOrderQuantity = validationResult.Data.QuoteOrderQuantity;

                        // 4. Check exchange balance
                        await ValidateExchangeBalanceAsync(asset.Exchange, exchange.QuoteAssetTicker, quoteOrderQuantity);

                        // 5. Check for previous orders and calculate remaining quantity
                        var remainingQuantity = await CalculateRemainingQuantityAsync(exchange, asset, payment, quoteOrderQuantity);

                        // 6. Check minimum notional
                        var minNotional = await GetMinNotionalAsync(exchange, asset);

                        if (remainingQuantity <= minNotional)
                        {
                            _logger.LogInformation("Order for asset {AssetId} already fully processed or remaining quantity is less than minimum notional value.",
                                allocation.AssetId);

                            return OrderResult.Success(exchange.Name, 0, assetId, quoteOrderQuantity, 0, "ALREADY_PROCESSED");
                        }

                        // 7. Place order and execute transaction
                        var orderResult = await PlaceOrderAndExecuteTransactionAsync(payment, allocation, asset, exchange, remainingQuantity, context.IdempotencyKey);
                        if (orderResult == null || !orderResult.IsSuccess)
                        {
                            throw new OrderExecutionException(
                                $"Failed to place order for {allocation.AssetTicker}: {orderResult?.ErrorMessage ?? "Oder result returned null"}",
                                exchange.Name);
                        }
                        return orderResult;
                    }
                    catch (Exception ex)
                    {

                        throw;
                    }
                })
                .WithPerformanceMonitoring(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(45))
                .OnSuccess(async (result) =>
                {
                    _logger.LogInformation("Successfully processed allocation for asset {AssetId}: OrderId={OrderId}, Status={Status}",
                        allocation.AssetId, result.OrderId, result.Status);
                })
                .OnError(async (ex) =>
                {
                    _logger.LogError("Failed to process allocation for asset {AssetId}: {ErrorMessage}",
                        allocation.AssetId, ex.Message);
                })
                .ExecuteAsync();
        }

        /// <summary>
        /// Validates allocation percentage and calculates order quantity
        /// </summary>
        private ResultWrapper<AllocationProcessingContext> ValidateAllocation(AllocationDto allocation, decimal netAmount)
        {
            if (allocation.PercentAmount <= 0 || allocation.PercentAmount > 100)
            {
                return ResultWrapper<AllocationProcessingContext>.Failure(
                    FailureReason.ValidationError,
                    $"Allocation must be between 0-100, received: {allocation.PercentAmount}",
                    validationErrors: new Dictionary<string, string[]>
                    {
                        ["PercentAmount"] = new[] { $"Allocation must be between 0-100, received: {allocation.PercentAmount}" }
                    });
            }

            decimal quoteOrderQuantity = Math.Round(netAmount * (allocation.PercentAmount / 100m), 2, MidpointRounding.ToZero);
            
            if (quoteOrderQuantity <= 0m)
            {
                return ResultWrapper<AllocationProcessingContext>.Failure(
                    FailureReason.ValidationError,
                    $"Order quantity must be positive, calculated: {quoteOrderQuantity}",
                    validationErrors: new Dictionary<string, string[]>
                    {
                        ["QuoteOrderQuantity"] = new[] { $"Order quantity must be positive, calculated: {quoteOrderQuantity}" }
                    });
            }



            return ResultWrapper<AllocationProcessingContext>.Success(new AllocationProcessingContext
            {
                QuoteOrderQuantity = quoteOrderQuantity
            });
        }

        /// <summary>
        /// Gets and validates asset data
        /// </summary>
        private async Task<AssetData> GetAndValidateAssetAsync(Guid assetId)
        {
            var assetResult = await _assetService.GetByIdAsync(assetId);
            if (assetResult == null || !assetResult.IsSuccess || assetResult.Data == null)
            {
                throw new AssetFetchException($"Failed to fetch asset Id {assetId}");
            }

            return assetResult.Data;
        }

        /// <summary>
        /// Gets and validates exchange for asset
        /// </summary>
        private IExchange GetAndValidateExchange(AssetData asset)
        {
            if (string.IsNullOrEmpty(asset.Exchange) ||
            !_exchangeService.Exchanges.TryGetValue(asset.Exchange, out var exchange))
            {
                throw new ValidationException("Invalid exchange",
                    new Dictionary<string, string[]>
                    {
                        ["Exchange"] = new[] { $"No exchange configured for asset {asset.Ticker}" }
                    });
            }

            return exchange;
        }

        /// <summary>
        /// Validates exchange has sufficient balance
        /// </summary>
        private async Task ValidateExchangeBalanceAsync(string exchangeName, string reserveAssetTicker, decimal amount)
        {
            var checkBalanceResult = await _balanceManagementService.CheckExchangeBalanceAsync(
                exchangeName, reserveAssetTicker, amount);

            if (!checkBalanceResult.IsSuccess)
            {
                throw new Exception($"Failed to check exchange balance: {checkBalanceResult.ErrorMessage}");
            }

            if (checkBalanceResult.Data is false)
            {
                throw new InsufficientBalanceException(checkBalanceResult.DataMessage ??
                    $"Insufficient balance in exchange to process order");
            }
        }

        /// <summary>
        /// Calculates remaining quantity after accounting for previous orders
        /// </summary>
        private async Task<decimal> CalculateRemainingQuantityAsync(
            IExchange exchange,
            AssetData asset,
            PaymentData payment,
            decimal quoteOrderQuantity)
        {
            var previousFilledSumResult = await _orderManagementService.GetPreviousOrdersSum(exchange, asset, payment);

            if (!previousFilledSumResult.IsSuccess)
            {
                throw new OrderFetchException($"Failed to calculate remaining order quantity for {payment.PaymentProviderId}: {previousFilledSumResult.ErrorMessage}");
            }

            return quoteOrderQuantity - previousFilledSumResult.Data;
        }

        /// <summary>
        /// Gets minimum notional value for the asset
        /// </summary>
        private async Task<decimal> GetMinNotionalAsync(IExchange exchange, AssetData asset)
        {
            var minNotionalResult = await _orderManagementService.GetMinNotional(exchange, asset);

            if (minNotionalResult == null || !minNotionalResult.IsSuccess)
            {
                await _logger.LogTraceAsync($"Failed to fetch minimum notional for {asset.Ticker} on {exchange.Name}: {minNotionalResult?.ErrorMessage ?? "Min notional returned null"}",
                    level: LogLevel.Error);
            }

            return minNotionalResult?.Data ?? 5m;
        }

        /// <summary>
        /// Places order and executes database transaction
        /// </summary>
        private async Task<OrderResult> PlaceOrderAndExecuteTransactionAsync(
            PaymentData payment,
            AllocationDto allocation,
            AssetData asset,
            IExchange exchange,
            decimal remainingQuantity,
            string idempotencyKey)
        {
            _logger.LogInformation("Placing order for {Ticker}: {Amount} {Currency} ({Percent}%)",
                asset.Ticker, remainingQuantity, exchange.QuoteAssetTicker, allocation.PercentAmount);

            // Place the order
            var placedOrderResult = await _orderManagementService.PlaceExchangeOrderAsync(
                exchange, asset.Ticker, remainingQuantity, payment.PaymentProviderId);

            if (placedOrderResult == null || !placedOrderResult.IsSuccess || placedOrderResult.Data is null)
            {
                throw new OrderExecutionException(
                    $"Unable to place order: {placedOrderResult?.ErrorMessage ?? "Order result returned null"}",
                    exchange.Name);
            }

            PlacedExchangeOrder placedOrder = placedOrderResult.Data;

            var insertResult = await RecordExchangeOrder(payment, allocation, placedOrder);
            if (insertResult != null)
            {
                await _eventService.PublishAsync(new ExchangeOrderCompletedEvent(insertResult, _logger.Context));
            }

            await _idempotencyService.StoreResultAsync(idempotencyKey, true);

            _logger.LogInformation("Successfully executed order {OrderId} for {Ticker}: {Quantity} @ {Price}",
                placedOrder.OrderId, asset.Ticker, placedOrder.QuantityFilled, placedOrder.Price);

            return OrderResult.Success(
                placedOrder.Exchange,
                placedOrder.OrderId,
                asset.Id.ToString(),
                placedOrder.QuoteQuantity,
                placedOrder.QuantityFilled,
                placedOrder.Status);
        }

        /// <summary>
        /// Records exchange order in database
        /// </summary>
        private async Task<ExchangeOrderData> RecordExchangeOrder(
            PaymentData payment,
            AllocationDto allocation,
            PlacedExchangeOrder placedOrder)
        {

            var quoteTicker = _exchangeService.Exchanges[placedOrder.Exchange].QuoteAssetTicker;
            var exchangeOrder = new ExchangeOrderData
            {
                UserId = payment.UserId,
                PaymentProviderId = payment.PaymentProviderId,
                SubscriptionId = payment.SubscriptionId,
                Exchange = placedOrder.Exchange,
                Side = placedOrder.Side.ToString().ToLowerInvariant(),
                PlacedOrderId = placedOrder.OrderId,
                AssetId = allocation.AssetId,
                Ticker = allocation.AssetTicker,
                Quantity = placedOrder.QuantityFilled,
                QuoteTicker = quoteTicker,
                QuoteQuantity = placedOrder.QuoteQuantity,
                QuoteQuantityFilled = placedOrder.QuoteQuantityFilled,
                Price = placedOrder.Price,
                Status = placedOrder.Status
            };

            var insertOrderResult = await _exchangeService.InsertAsync(exchangeOrder);
            if (insertOrderResult == null || !insertOrderResult.IsSuccess || !insertOrderResult.Data.IsSuccess)
            {
                await _logger.LogTraceAsync($"Failed to create exchange order record: {insertOrderResult?.ErrorMessage ?? "Unknown error"}", action: "RecordExchangeOrder", level: LogLevel.Error);
            }

            return insertOrderResult!.Data.Documents.First();
        }

        /// <summary>
        /// Evaluates final results and determines success/failure
        /// </summary>
        private ResultWrapper<IEnumerable<OrderResult>> EvaluateFinalResults(List<OrderResult> orderResults)
        {
            if (orderResults.All(r => !r.IsSuccess))
            {
                return ResultWrapper<IEnumerable<OrderResult>>.Failure(
                    FailureReason.OrderExecutionFailed,
                    $"All orders failed to process: {string.Join("; ", orderResults.Select(r => r.ErrorMessage))}"
                    );
            }

            return ResultWrapper<IEnumerable<OrderResult>>.Success(orderResults);
        }

        #endregion

        #region Context Classes

        private class PaymentProcessingContext
        {
            public string IdempotencyKey { get; set; }
            public decimal NetAmount { get; set; }
        }

        private class AllocationProcessingContext
        {
            public decimal QuoteOrderQuantity { get; set; }
        }

        #endregion
    }
}