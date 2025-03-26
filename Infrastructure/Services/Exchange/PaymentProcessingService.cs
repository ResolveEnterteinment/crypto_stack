using Application.Interfaces;
using Application.Interfaces.Exchange;
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
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Polly;

namespace Infrastructure.Services.Exchange
{
    public class PaymentProcessingService : IPaymentProcessingService, INotificationHandler<PaymentReceivedEvent>
    {
        private readonly ISubscriptionService _subscriptionService;
        private readonly IAssetService _assetService;
        private readonly IBalanceService _balanceService;
        private readonly IExchangeService _exchangeService;
        private readonly IBalanceManagementService _balanceManagementService;
        private readonly ITransactionService _transactionService;
        private readonly IOrderManagementService _orderManagementService;
        private readonly IEventService _eventService;
        private readonly ILogger<BalanceManagementService> _logger;
        public PaymentProcessingService(
            ISubscriptionService subscriptionService,
            IAssetService assetService,
            IBalanceService balanceService,
            ITransactionService transactionService,
            IExchangeService exchangeService,
            IBalanceManagementService balanceManagementService,
            IOrderManagementService orderManagementService,
            IEventService eventService,
            ILogger<BalanceManagementService> logger
            )
        {
            _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
            _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
            _balanceService = balanceService ?? throw new ArgumentNullException(nameof(balanceService));
            _transactionService = transactionService ?? throw new ArgumentNullException(nameof(transactionService));
            _exchangeService = exchangeService ?? throw new ArgumentNullException(nameof(exchangeService));
            _balanceManagementService = balanceManagementService ?? throw new ArgumentNullException(nameof(balanceManagementService));
            _orderManagementService = orderManagementService ?? throw new ArgumentNullException(nameof(orderManagementService));
            _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task Handle(PaymentReceivedEvent notification, CancellationToken cancellationToken)
        {
            _logger.LogInformation($"Notification {notification.GetType().Name} received with payment id #{notification.Payment} and event id #{notification.EventId}");
            try
            {
                var payment = notification.Payment;
                if (payment == null)
                {
                    _logger.LogWarning($"Invalid payment data. Payment can not be null: {notification.Payment}");
                    return;
                }

                // Define Polly retry policy
                var policy = Policy
                    .Handle<Exception>()
                    .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                        onRetry: (ex, time, retryCount, context) =>
                            _logger.LogWarning("Retry {RetryCount} for PaymentId: {PaymentId} due to {Exception}",
                                retryCount, notification.Payment, ex.Message));

                // Execute with retries
                await policy.ExecuteAsync(async () =>
                {
                    var result = await ProcessPayment(payment);
                    if (!result.IsSuccess)
                    {
                        throw new Exception($"Order processing failed: {string.Join(", ", result.Data!.Where(o => !o.IsSuccess).Select(o => $"Asset id #{o.AssetId}: {o.ErrorMessage}"))}");
                    }
                });

                // Mark event as processed
                await _eventService.UpdateOneAsync(notification.EventId, new
                {
                    Processed = true,
                    ProcessedAt = DateTime.UtcNow
                });

                _logger.LogInformation("Successfully processed payment: {PaymentId}", notification.Payment.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to process PaymentReceivedEvent for PaymentId: {notification.Payment} after retries: {ex.Message}");
                // Event remains unprocessed in MongoDB for recovery
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
                        #region Validation
                        if (alloc.PercentAmount <= 0 || alloc.PercentAmount > 100)
                            throw new ArgumentOutOfRangeException(nameof(alloc.PercentAmount), "Allocation must be between 0-100.");

                        decimal quoteOrderQuantity = netAmount * (alloc.PercentAmount / 100m);

                        if (quoteOrderQuantity <= 0m)
                            throw new ArgumentException($"Quote order quantity must be positive. Value: {quoteOrderQuantity}");
                        #endregion

                        var asset = await _assetService.GetByIdAsync(alloc.AssetId);
                        var exchange = _exchangeService.Exchanges[asset.Exchange];
                        var reserveAssetTicker = exchange.ReserveAssetTicker;

                        // Check exchange fiat balance
                        var checkBalanceResult = await exchange.CheckBalanceHasEnough(reserveAssetTicker, quoteOrderQuantity);

                        if (!checkBalanceResult.IsSuccess)
                        {
                            throw new Exception($"Failed to check exchange balance: {checkBalanceResult.ErrorMessage}");
                        }

                        if (checkBalanceResult.Data is false)
                        {
                            throw new InsufficientBalanceException(checkBalanceResult.DataMessage);
                        }

                        //Check previous filled orders. If present process only the remaining amount.
                        var previousFilledSumResult = await _orderManagementService.GetPreviousOrdersSum(exchange, asset, payment);

                        if (!previousFilledSumResult.IsSuccess)
                        {
                            throw new Exception($"Failed to fetch prervious orders for  {payment.PaymentProviderId}");
                        }

                        var remainingQuoteOrderQuantity = quoteOrderQuantity - previousFilledSumResult.Data;

                        var placedOrderResult = await _orderManagementService.PlaceExchangeOrderAsync(exchange, asset.Ticker, remainingQuoteOrderQuantity, payment.PaymentProviderId);

                        if (!placedOrderResult.IsSuccess || placedOrderResult.Data is null || placedOrderResult.Data.Status == OrderStatus.Failed)
                        {
                            throw new Exception($"Unable to place order: {placedOrderResult.ErrorMessage}");
                        }

                        var placedOrder = placedOrderResult.Data;

                        //Handle dust quantity if present
                        await _orderManagementService.HandleDustAsync(placedOrder);

                        // Atomic insert of order, transaction and event
                        using (var session = await _exchangeService.StartDBSession())
                        {
                            session.StartTransaction();
                            try
                            {
                                var insertOrderResult = await _exchangeService.InsertOneAsync(new ExchangeOrderData()
                                {
                                    UserId = payment.UserId,
                                    PaymentProviderId = payment.PaymentProviderId,
                                    SubscriptionId = payment.SubscriptionId,
                                    PlacedOrderId = placedOrder?.OrderId,
                                    AssetId = alloc.AssetId,
                                    QuoteQuantity = placedOrder.QuoteQuantity,
                                    QuoteQuantityFilled = placedOrder.QuoteQuantityFilled,
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

                                var updateBalanceResult = await _balanceService.UpsertBalanceAsync(payment.UserId, new BalanceData()
                                {
                                    UserId = payment.UserId,
                                    AssetId = alloc.AssetId,
                                    Available = placedOrder.QuantityFilled
                                }, session);

                                if (updateBalanceResult is null || !updateBalanceResult.IsSuccess || updateBalanceResult.Data is null)
                                {
                                    //TO-DO: Fallback save update BalanceData locally to reconcile later.
                                    throw new MongoException($"Failed to update balances: {updateBalanceResult?.ErrorMessage ?? "Balance update result returned null"}");
                                }

                                var insertTransactionResult = await _transactionService.InsertOneAsync(new TransactionData()
                                {
                                    UserId = payment.UserId,
                                    PaymentProviderId = payment.PaymentProviderId,
                                    SubscriptionId = payment.SubscriptionId,
                                    BalanceId = updateBalanceResult.Data.Id,
                                    SourceName = exchange.Name,
                                    SourceId = placedOrder.OrderId.ToString(),
                                    Action = "Exchange Order: Buy",
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

                        _logger.LogInformation("Order created for {Ticker}, %{OrderId}", asset.Ticker, alloc.PercentAmount);
                    }
                    catch (Exception ex)
                    {
                        orderResults.Add(OrderResult.Failure(alloc?.AssetId.ToString(), FailureReasonExtensions.FromException(ex), ex.Message));
                        _logger.LogError(ex, "Failed to process order: {Message}", ex.Message);
                    }
                }
                return ResultWrapper<IEnumerable<OrderResult>>.Success(orderResults);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process payment: {Message}", ex.Message);
                return ResultWrapper<IEnumerable<OrderResult>>.Failure(FailureReasonExtensions.FromException(ex), $"{ex.Message}");
            }
        }
    }
}
