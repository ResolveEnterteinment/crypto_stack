using Application.Contracts.Responses.Exchange;
using Application.Interfaces;
using AspNetCore.Identity.MongoDbCore.Infrastructure;
using Binance.Net.Objects.Models.Spot;
using BinanceLibrary;
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

        public ExchangeService(IOptions<BinanceSettings> binanceSettings, IOptions<MongoDbSettings> mongoDbSettings, ISubscriptionService subscriptionService, ICoinService coinService, ILogger<ExchangeService> logger)
        {
            _binanceService = new BinanceService(binanceSettings);
            _subscriptionService = subscriptionService;
            _coinService = coinService;
            // Assume your MongoDB database has collections named "transactions" and "subscriptions"
            var mongoClient = new MongoClient(mongoDbSettings.Value.ConnectionString);

            var mongoDatabase = mongoClient.GetDatabase(
                mongoDbSettings.Value.DatabaseName);

            _exchangeOrders = mongoDatabase.GetCollection<ExchangeOrderData>("exchange_orders");
            _logger = logger;
        }

        /// <summary>
        /// Creates an exchange order.
        /// </summary>
        /// <param name="symbol">The trading pair (e.g., BTCUSDT).</param>
        /// <param name="quantity">The amount of the quote asset (e.g., USDT) to spend.</param>
        /// <param name="side">Order side: BUY or SELL. Default value is BUY.</param>
        /// <param name="type">Order type. Default value is MARKET.</param>
        /// <returns>The order details if successful; otherwise, null.</returns>
        /// <summary>
        /// Creates an exchange order.
        /// </summary>
        /// <param name="symbol">The trading pair (e.g., BTCUSDT).</param>
        /// <param name="quantity">The amount of the quote asset (e.g., USDT) to spend.</param>
        /// <param name="side">Order side: BUY or SELL. Default value is BUY.</param>
        /// <param name="type">Order type. Default value is MARKET. (Currently unused)</param>
        /// <returns>The order details if successful; otherwise, null.</returns>
        private async Task<ExchangeOrderData?> CreateOrder(string ticker, decimal quantity, string side = "BUY", string type = "MARKET")
        {
            if (!(quantity > 0)) throw new ArgumentException("Order quantity must be greater than 0!");
            try
            {
                BinancePlacedOrder? order = null;
                var symbol = ticker + "USDT";

                // Normalize the side value and handle only BUY or SELL.
                if (side.Equals("BUY", StringComparison.OrdinalIgnoreCase))
                {
                    order = await _binanceService.PlaceSpotMarketBuyOrder(symbol, quantity);
                }
                else if (side.Equals("SELL", StringComparison.OrdinalIgnoreCase))
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
                    throw new CreateOrderException(string.Format("Order creation failed for symbol: {0}", symbol));
                }

                var exchangeOrder = new ExchangeOrderData
                {
                    _id = ObjectId.GenerateNewId(),              // New unique identifier.
                    CreateTime = order.CreateTime,
                    UserId = ObjectId.Empty,              // TODO: Replace with the actual user id.
                    TranscationId = ObjectId.Empty,       // TODO: Replace with the actual transaction id.
                    CryptoId = _coinService.GetCryptoFromSymbol(order.Symbol)._id,            // TODO: Replace with the actual crypto id.
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
                _logger.LogError("Error while creating order: {0} ", ex.Message);
                return null;
            }
        }

        public async Task<IEnumerable<ExchangeOrderResponse>?> ProcessTransaction(TransactionData transaction)
        {
            var orderResponses = new List<ExchangeOrderResponse>();

            try
            {
                var allocations = await _subscriptionService.GetCoinAllocationsAsync(transaction.SubscriptionId);
                if (!allocations.Any())
                {
                    throw new KeyNotFoundException("Error fetching coin allocations for subscription.");
                }
                foreach (var alloc in allocations)
                {
                    // Assume alloc.CoinId is the symbol for the trading pair. Adjust if necessary.
                    CoinData? coinData = _coinService.GetCoinData(alloc.CoinId) ?? throw new KeyNotFoundException(string.Format("Unable to fetch coin data with id #{0}", alloc.CoinId.ToString()));
                    decimal orderQuantity = transaction.NetAmount * (alloc.Allocation / 100);
                    var order = await CreateOrder(coinData.Ticker, orderQuantity);
                    if (order != null)
                    {
                        order.UserId = transaction.UserId;
                        order.TranscationId = transaction._id;
                        await _exchangeOrders.InsertOneAsync(order);
                        orderResponses.Add(new ExchangeOrderResponse(true, string.Format("Order created: id: {0}, ticker: {1}, price: {2}, quantity: {3}, status: {4}", order.OrderId, coinData.Ticker, order.Price, order.Quantity, order.Status)));
                    }
                    else
                    {
                        orderResponses.Add(new ExchangeOrderResponse(false, "Failed to create exchange order"));
                    }
                }
                return orderResponses;
            }
            catch (Exception ex)
            {
                // Depending on requirements, you might choose to throw here or return partial results.
                throw new CreateOrderException(string.Format("Error processing transaction with subscription #{0}: {1}", transaction.SubscriptionId, ex.Message));
            }
        }

        /// <summary>
        /// Displays order details in a readable format.
        /// </summary>
        /// <param name="order">The Binance order details.</param>
        public static void DisplayOrderDetails(BinancePlacedOrder order)
        {
            Console.WriteLine(new string('-', 40));
            Console.WriteLine($"Order ID:                   {order.Id}");
            // Assuming BinancePlacedOrder has a ClientOrderId property.
            Console.WriteLine($"Client Order ID:            {order.ClientOrderId}");
            Console.WriteLine($"Symbol:                     {order.Symbol}");
            Console.WriteLine($"Side:                       {order.Side}");
            Console.WriteLine($"Price:                      {order.Price}");
            Console.WriteLine($"Executed Quantity:          {order.QuoteQuantity}");
            // If available, use the cumulative quote quantity property.
            //Console.WriteLine($"Cumulative Quote Quantity:  {order.CummulativeQuoteQty}");
            Console.WriteLine($"Order Create Time:          {order.CreateTime}");
            Console.WriteLine($"Status:                     {order.Status}");
            Console.WriteLine(new string('-', 40));
        }
    }
}
