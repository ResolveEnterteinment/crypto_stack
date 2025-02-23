using Application.Interfaces;
using AspNetCore.Identity.MongoDbCore.Infrastructure;
using Binance.Net.Objects.Models.Spot;
using BinanceLibrary;
using Domain.Constants;
using Domain.DTOs;
using Domain.Models.Crypto;
using Domain.Models.Exchange;
using Domain.Models.Subscription;
using Domain.Models.Transaction;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Infrastructure.Services
{
    public class ExchangeService : BaseService<ExchangeOrderData>, IExchangeService
    {
        private IBinanceService _binanceService;
        private readonly ISubscriptionService _subscriptionService;
        private readonly ICoinService _coinService;

        public ExchangeService(
            IOptions<BinanceSettings> binanceSettings,
            IOptions<MongoDbSettings> mongoDbSettings,
            IMongoClient mongoClient, // Injected singleton MongoClient
            ISubscriptionService subscriptionService,
            ICoinService coinService,
            ILogger<ExchangeService> logger) : base(mongoClient, mongoDbSettings, "exchange_orders", logger)
        {
            // Use the injected IBinanceService instead of instantiating inline.
            //_binanceService = binanceService;
            _binanceService = CreateBinanceService(binanceSettings); // Use factory method
            _subscriptionService = subscriptionService;
            _coinService = coinService;
        }

        // Protected virtual factory method for instantiation
        protected virtual IBinanceService CreateBinanceService(IOptions<BinanceSettings> settings)
        {
            return new BinanceService(settings);
        }

        /// <summary>
        /// Creates an exchange order asynchronously.
        /// </summary>
        /// <param name="ticker">The trading pair ticker (e.g., BTC).</param>
        /// <param name="quantity">The amount of the quote asset (e.g., USDT) to spend.</param>
        /// <param name="side">Order side (BUY or SELL). Default is BUY.</param>
        /// <param name="type">Order type. Default is MARKET.</param>
        /// <returns>The created ExchangeOrderData if successful; otherwise, null.</returns>
        public async Task<PlacedOrderResult?> PlaceExchangeOrderAsync(CoinData coin, decimal quantity, string side = OrderSide.Buy, string type = "MARKET")
        {
            try
            {
                if (quantity <= 0m)
                {
                    throw new ArgumentOutOfRangeException(nameof(quantity), $"Quantity must be greater than zero. Provided value is {quantity}");
                }

                BinancePlacedOrder order = new();
                var symbol = coin.Ticker + "USDT";

                // Call the appropriate Binance service method based on the OrderSide enum.
                if (side == OrderSide.Buy)
                {
                    order = await _binanceService.PlaceSpotMarketBuyOrder(symbol, quantity);
                }
                else if (side == OrderSide.Sell)
                {
                    order = await _binanceService.PlaceSpotMarketSellOrder(symbol, quantity);
                }
                else
                {
                    throw new ArgumentException("Invalid order side. Allowed values are BUY or SELL.", nameof(side));
                }

                // Log order details using structured logging instead of Console.WriteLine.
                LogOrderDetails(order);

                var placedOrder = new PlacedOrderResult()
                {
                    CryptoId = coin._id,
                    QuoteQuantity = order.QuoteQuantity,
                    Price = order.AverageFillPrice,
                    Quantity = order.QuantityFilled,
                    OrderId = order.Id,
                    Status = order.Status.ToString(),
                };

                _logger.LogInformation($"Order created successfully for symbol: {symbol}, OrderId: {order.Id}, Status: {order.Status}");
                return placedOrder;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to create order: {ex.Message}");
                return null;
            }
        }

        public async Task<AllocationOrdersResult> ProcessTransaction(TransactionData transaction)
        {
            var orderResults = new List<OrderResult>();
            var netAmount = transaction.NetAmount;
            if (netAmount <= 0)
            {
                orderResults.Add(OrderResult.Failure(null, FailureReason.ValidationError, $"Invalid transaction net amount. Transaction net amount must be greater than zero. Provided value is {netAmount}"));
                return new AllocationOrdersResult(orderResults.AsReadOnly());
            }

            FetchAllocationsResult fetchAllocationsResult = await _subscriptionService.GetAllocationsAsync(transaction.SubscriptionId);
            if (!fetchAllocationsResult.AllFilled)
            {
                orderResults.Add(OrderResult.Failure(null, FailureReason.ValidationError, $"Unable to fetch coin allocations due to {fetchAllocationsResult.FailureReason}: {fetchAllocationsResult.ErrorMessage}"));
                return new AllocationOrdersResult(orderResults.AsReadOnly());
            }

            foreach (var alloc in fetchAllocationsResult.Allocations)
            {
                try
                {
                    if (alloc.PercentAmount <= 0 || alloc.PercentAmount > 100)
                        throw new ArgumentOutOfRangeException(nameof(alloc.PercentAmount), "Allocation must be between 0-100.");

                    decimal quoteOrderQuantity = netAmount * (alloc.PercentAmount / 100m);
                    if (quoteOrderQuantity <= 0)
                        throw new ArgumentException($"Quote order quantity must be positive. Value: {quoteOrderQuantity}");

                    var coinData = await _coinService.GetByIdAsync(alloc.CoinId);
                    if (coinData is null)
                        throw new KeyNotFoundException($"Coin data not found for id #{alloc.CoinId}");

                    var symbol = coinData.Ticker + "USDT";
                    PlacedOrderResult? placedOrder = await PlaceExchangeOrderAsync(coinData, quoteOrderQuantity);
                    if (placedOrder is null)
                        throw new Exception("Order placement returned null");

                    ExchangeOrderData orderData = new ExchangeOrderData()
                    {
                        UserId = transaction.UserId,
                        TranscationId = transaction._id,
                        OrderId = placedOrder.OrderId,
                        CryptoId = placedOrder.CryptoId,
                        QuoteQuantity = placedOrder.QuoteQuantity,
                        Quantity = placedOrder.Quantity,
                        Price = placedOrder.Price,
                        Status = placedOrder.Status
                    };
                    var balance = new BalanceData()
                    {
                        CoinId = placedOrder.CryptoId,
                        Quantity = placedOrder.Quantity
                    };
                    var insertResult = await InsertAsync(orderData);
                    var updateBalanceResult = await _subscriptionService.UpdateBalances(transaction.SubscriptionId, new List<BalanceData>() { balance });
                    if (insertResult is null || !insertResult.IsAcknowledged)
                        throw new Exception($"Failed to create order record: {insertResult?.ErrorMessage}");

                    orderResults.Add(OrderResult.Success(placedOrder.OrderId, insertResult.InsertedId?.ToString(), coinData._id.ToString(), placedOrder.QuoteQuantity, placedOrder.Quantity, placedOrder.Status));
                    _logger.LogInformation("Order created for {Symbol}, OrderId: {OrderId}", symbol, placedOrder.OrderId);
                }
                catch (Exception ex)
                {
                    string reason = ex switch
                    {
                        ArgumentOutOfRangeException => FailureReason.ValidationError,
                        ArgumentException => FailureReason.ValidationError,
                        KeyNotFoundException => FailureReason.DataNotFound,
                        _ when ex.Message.Contains("Binance") => FailureReason.ExchangeApiError,
                        _ when ex.Message.Contains("insert") => FailureReason.DatabaseError,
                        _ => FailureReason.Unknown
                    };
                    orderResults.Add(OrderResult.Failure(alloc.CoinId.ToString(), reason, ex.Message));
                    _logger.LogError(ex, $"Failed to process order: {ex.Message}");
                }
            }

            return new AllocationOrdersResult(orderResults.AsReadOnly());
        }

        /// <summary>
        /// Logs order details using structured logging.
        /// </summary>
        /// <param name="order">The BinancePlacedOrder instance.</param>
        private static void LogOrderDetails(BinancePlacedOrder order)
        {
            // Instead of using Console.WriteLine, use structured logging.
            // In a real-world scenario, consider passing an ILogger instance.
            Console.WriteLine(new string('-', 50));
            Console.WriteLine($"Order ID:                   {order.Id}");
            Console.WriteLine($"Client Order ID:            {order.ClientOrderId}");
            Console.WriteLine($"Symbol:                     {order.Symbol}");
            Console.WriteLine($"Side:                       {order.Side}");
            Console.WriteLine($"Price:                      {order.Price}");
            Console.WriteLine($"Quote Quantity:             {order.QuoteQuantity}");
            Console.WriteLine($"Order Quantity:             {order.QuantityFilled}");
            Console.WriteLine($"Create Time:                {order.CreateTime}");
            Console.WriteLine($"Status:                     {order.Status}");
            Console.WriteLine(new string('-', 50));
        }
    }
}
