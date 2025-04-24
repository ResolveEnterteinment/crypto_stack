using Application.Interfaces;
using Application.Interfaces.Exchange;
using Domain.Constants;
using Domain.DTOs.Exchange;
using Domain.Exceptions;
using Domain.Models.Exchange;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Infrastructure.Services.Exchange
{
    public class OrderReconciliationService : IOrderReconciliationService
    {
        private readonly IExchangeService _exchangeService;
        private readonly IEventService _eventService;
        private readonly ILogger<BalanceManagementService> _logger;
        public OrderReconciliationService(
            IExchangeService exchangeService,
            IEventService eventService,
            ILogger<BalanceManagementService> logger
            )
        {
            _exchangeService = exchangeService ?? throw new ArgumentNullException(nameof(exchangeService));
            _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // Reconciliation method for WebSocket reliability
        public async Task ReconcilePendingOrdersAsync()
        {
            var filter = Builders<ExchangeOrderData>.Filter.Eq(o => o.Status, OrderStatus.Pending);
            var pendingOrdersResult = await _exchangeService.GetManyAsync(filter);
            if (pendingOrdersResult == null || !pendingOrdersResult.IsSuccess)
            {
                throw new OrderFetchException("Failed to fetch pending orders");
            }
            var pendingOrders = pendingOrdersResult.Data;

            foreach (var order in pendingOrders)
            {
                try
                {
                    if (order.PlacedOrderId is null) throw new ArgumentNullException(nameof(order.PlacedOrderId));
                    PlacedExchangeOrder exchangeOrder = await _exchangeService.Exchanges[order.Exchange].GetOrderInfoAsync((long)order.PlacedOrderId); // TO-DO: Create GetOrderStatus dunction

                    if (exchangeOrder is null) throw new ArgumentNullException(nameof(exchangeOrder));

                    var update = Builders<ExchangeOrderData>.Update
                        .Set(o => o.Status, exchangeOrder.Status.ToString()) // Map Binance status to your enum/string
                        .Set(o => o.Quantity, exchangeOrder.QuantityFilled)
                        .Set(o => o.Price, exchangeOrder.Price);

                    await _exchangeService.UpdateAsync(order.Id, new
                    {
                        Status = exchangeOrder.Status.ToString(),
                        Quantity = exchangeOrder.QuantityFilled,
                        exchangeOrder.Price
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

        private async Task HandleFailedOrderAsync(ExchangeOrderData order)
        {
            if (order.RetryCount >= 3)
            {
                _logger.LogError("Max retries reached for OrderId: {OrderId}", order.PlacedOrderId);
                await _exchangeService.UpdateAsync(order.Id, new
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
            await _exchangeService.InsertAsync(order);
        }
    }
}
