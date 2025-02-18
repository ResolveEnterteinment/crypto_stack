using Application.Contracts.Responses.Exchange;
using Application.Interfaces;
using AspNetCore.Identity.MongoDbCore.Infrastructure;
using Binance.Net.Objects.Models.Spot;
using BinanceLibrary;
using Domain.Constants;
using Domain.DTOs;
using Domain.Models.Crypto;
using Domain.Models.Exchange;
using Domain.Models.Transaction;
using Infrastructure.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Infrastructure.Services
{
    public class ExchangeService : IExchangeService
    {
        private readonly IBinanceService _binanceService;
        private readonly ISubscriptionService _subscriptionService;
        private readonly ICoinService _coinService;
        private readonly IMongoCollection<ExchangeOrderData> _exchangeOrders;
        private readonly ILogger<ExchangeService> _logger;

        public ExchangeService(
            IOptions<BinanceSettings> binanceSettings,
            IOptions<MongoDbSettings> mongoDbSettings,
            IMongoClient mongoClient, // Injected singleton MongoClient
            ISubscriptionService subscriptionService,
            ICoinService coinService,
            ILogger<ExchangeService> logger)
        {
            // Use the injected IBinanceService instead of instantiating inline.
            //_binanceService = binanceService;
            _binanceService = new BinanceService(binanceSettings);
            _subscriptionService = subscriptionService;
            _coinService = coinService;

            var databaseName = mongoDbSettings.Value.DatabaseName;
            var mongoDatabase = mongoClient.GetDatabase(databaseName);
            _exchangeOrders = mongoDatabase.GetCollection<ExchangeOrderData>("exchange_orders");

            _logger = logger;
        }

        /// <summary>
        /// Creates an exchange order asynchronously.
        /// </summary>
        /// <param name="ticker">The trading pair ticker (e.g., BTC).</param>
        /// <param name="quantity">The amount of the quote asset (e.g., USDT) to spend.</param>
        /// <param name="side">Order side (BUY or SELL). Default is BUY.</param>
        /// <param name="type">Order type. Default is MARKET.</param>
        /// <returns>The created ExchangeOrderData if successful; otherwise, null.</returns>
        private async Task<ExchangeOrderData?> CreateOrderAsync(string ticker, decimal quantity, string side = OrderSideConstants.Buy, string type = "MARKET")
        {
            try
            {
                if (quantity <= 0m)
                {
                    throw new ArgumentOutOfRangeException(nameof(quantity), $"Quantity must be greater than zero. Provided value is {quantity}");
                }

                BinancePlacedOrder? order = null;
                var symbol = ticker + "USDT";

                // Call the appropriate Binance service method based on the OrderSide enum.
                if (side == OrderSideConstants.Buy)
                {
                    order = await _binanceService.PlaceSpotMarketBuyOrder(symbol, quantity);
                }
                else if (side == OrderSideConstants.Sell)
                {
                    order = await _binanceService.PlaceSpotMarketSellOrder(symbol, quantity);
                }
                else
                {
                    throw new ArgumentException("Invalid order side. Allowed values are BUY or SELL.", nameof(side));
                }

                if (order == null)
                {
                    _logger.LogError("Order creation failed for symbol: {Symbol}", symbol);
                    throw new OrderCreationException($"Order creation failed for symbol: {symbol}");
                }

                // Log order details using structured logging instead of Console.WriteLine.
                LogOrderDetails(order);

                // Retrieve crypto data asynchronously.
                var crypto = await _coinService.GetCryptoFromSymbolAsync(order.Symbol);
                var exchangeOrder = new ExchangeOrderData
                {
                    _id = ObjectId.GenerateNewId(),
                    CreatedAt = order.CreateTime,
                    UserId = ObjectId.Empty,          // TODO: Replace with the actual user id.
                    TranscationId = ObjectId.Empty,   // TODO: Replace with the actual transaction id.
                    CryptoId = crypto?._id ?? ObjectId.Empty, // TODO: Adjust as needed.
                    QuoteQuantity = order.QuoteQuantity,
                    Price = order.AverageFillPrice,
                    Quantity = order.QuantityFilled,
                    OrderId = order.Id,
                    Status = order.Status.ToString(),
                };

                _logger.LogInformation("Order created successfully for symbol: {Symbol}, OrderId: {OrderId}", symbol, order.Id);
                return exchangeOrder;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create order: {Message}", ex.Message);
                throw new OrderCreationException($"Failed to create order: {ex.Message}", ex);
            }
        }

        public async Task<IEnumerable<ExchangeOrderResponse>?> ProcessTransaction(TransactionData transaction)
        {
            var netAmount = transaction.NetAmount;
            if (netAmount <= 0)
                throw new ArgumentOutOfRangeException(nameof(netAmount), $"Net transaction amount must be greater than zero. Provided value is {netAmount}");

            var orderResponses = new List<ExchangeOrderResponse>();

            var allocations = await _subscriptionService.GetCoinAllocationsAsync(transaction.SubscriptionId);
            if (!allocations.Any())
            {
                _logger.LogError("Unable to fetch coin allocations for subscription #{SubscriptionId}", transaction.SubscriptionId);
                throw new KeyNotFoundException($"Unable to fetch coin allocations for subscription #{transaction.SubscriptionId}");
            }

            foreach (var alloc in allocations)
            {
                try
                {
                    if (alloc.PercentAmount <= 0 || alloc.PercentAmount > 100) throw new ArgumentOutOfRangeException(nameof(alloc.PercentAmount), "Allocation must be a number between 0-100.");
                    decimal quoteOrderQuantity = netAmount * (alloc.PercentAmount / 100m); //division of decimal, don't forget to put m at the end, otherwise you get 0!!! percent/100m

                    CoinData? coinData = await _coinService.GetCoinDataAsync(alloc.CoinId)
                        ?? throw new KeyNotFoundException($"Coin data not found by id #{alloc.CoinId}");

                    // Create order asynchronously using BUY as default. Adjust side if needed.
                    var order = await CreateOrderAsync(coinData.Ticker, quoteOrderQuantity);
                    if (order == null)
                        throw new OrderCreationException("Order creation returned null.");

                    order.UserId = transaction.UserId;
                    order.TranscationId = transaction._id;

                    await _exchangeOrders.InsertOneAsync(order);

                    orderResponses.Add(new ExchangeOrderResponse(
                        true,
                        $"Order created: id: {order.OrderId}, ticker: {coinData.Ticker}, price: {order.Price}, quantity: {order.Quantity}, status: {order.Status}"
                    ));
                    _logger.LogInformation(orderResponses.ToString());
                }
                catch (Exception ex)
                {
                    orderResponses.Add(new ExchangeOrderResponse(false, $"Failed to create exchange order: {ex.Message}"));
                }
            }
            return orderResponses;
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
            Console.WriteLine($"Executed Quantity:          {order.QuoteQuantity}");
            Console.WriteLine($"Order Create Time:          {order.CreateTime}");
            Console.WriteLine($"Status:                     {order.Status}");
            Console.WriteLine(new string('-', 50));
        }
    }
}
