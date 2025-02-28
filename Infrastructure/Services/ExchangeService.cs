using Application.Interfaces;
using AspNetCore.Identity.MongoDbCore.Infrastructure;
//using Binance.Net.Enums;
using Binance.Net.Objects.Models.Spot;
using BinanceLibrary;
using Domain.Constants;
using Domain.DTOs;
using Domain.Models.Balance;
using Domain.Models.Crypto;
using Domain.Models.Exchange;
using Domain.Models.Transaction;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Infrastructure.Services
{
    public class ExchangeService : BaseService<ExchangeOrderData>, IExchangeService
    {
        protected IBinanceService _binanceService;
        private readonly ISubscriptionService _subscriptionService;
        private readonly IBalanceService _balanceService;
        private readonly ICoinService _coinService;
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
            ISubscriptionService subscriptionService,
            IBalanceService balanceService,
            ICoinService coinService,
            ILogger<ExchangeService> logger) : base(mongoClient, mongoDbSettings, "exchange_orders", logger)
        {
            _binanceService = CreateBinanceService(binanceSettings);
            _subscriptionService = subscriptionService;
            _balanceService = balanceService;
            _coinService = coinService;
        }

        protected virtual IBinanceService CreateBinanceService(IOptions<BinanceSettings> settings, IBinanceService? refService = null)
        {
            return refService ?? new BinanceService(settings);
        }

        public async Task<ResultWrapper<PlacedExchangeOrder>> PlaceExchangeOrderAsync(CoinData coin, decimal quantity, ObjectId subcriptionId, string side = Domain.Constants.OrderSide.Buy, string type = "MARKET")
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

        public async Task<AllocationOrdersResult> ProcessTransaction(TransactionData transaction)
        {
            var orderResults = new List<OrderResult>();
            var netAmount = transaction.NetAmount;

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

            var fetchAllocationsResult = await _subscriptionService.GetAllocationsAsync(transaction.SubscriptionId);
            if (!fetchAllocationsResult.IsSuccess)
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

                    var coinData = await _coinService.GetByIdAsync(alloc.CoinId);
                    if (coinData is null)
                        throw new KeyNotFoundException($"Unable to fetch coin #{alloc.CoinId} data.");

                    var symbol = coinData.Ticker + "USDT";
                    var placedOrder = await PlaceExchangeOrderAsync(coinData, quoteOrderQuantity, transaction.SubscriptionId);
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
                                UserId = transaction.UserId,
                                TransactionId = transaction._id,
                                OrderId = placedOrder.Data?.OrderId,
                                CryptoId = placedOrder.Data.CryptoId,
                                QuoteQuantity = placedOrder.Data.QuoteQuantity,
                                QuoteQuantityFilled = placedOrder.Data.QuoteQuantityFilled,
                                Quantity = placedOrder.Data.QuantityFilled,
                                Price = placedOrder.Data.Price,
                                Status = placedOrder.Data.Status
                            };

                            var insertResult = await InsertOneAsync(insertOrderData);
                            if (!insertResult.IsAcknowledged)
                            {
                                //TO-DO: Fallback save ExchangeOrderData locally to reconcile later.
                                _logger.LogError($"Failed to create order record: {insertResult?.ErrorMessage}");
                                throw new MongoException($"Failed to create order record: {insertResult?.ErrorMessage}");
                            }

                            var updateBalance = new BalanceData()
                            {
                                UserId = transaction.UserId,
                                SubscriptionId = transaction.SubscriptionId,
                                CoinId = placedOrder.Data.CryptoId,
                                Quantity = placedOrder.Data.QuantityFilled
                            };
                            var updateBalanceResult = await _balanceService.UpsertBalanceAsync(insertOrderData._id, transaction.SubscriptionId, updateBalance);
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
                    orderResults.Add(OrderResult.Failure(alloc?.CoinId.ToString(), reason, ex.Message));
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
            var pendingOrders = await _collection.Find(o => o.Status == OrderStatus.Pending).ToListAsync();
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

                    await _collection.UpdateOneAsync(
                        Builders<ExchangeOrderData>.Filter.Eq(o => o._id, order._id),
                        update);

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
                await _collection.UpdateOneAsync(
                    Builders<ExchangeOrderData>.Filter.Eq(o => o._id, order._id),
                    Builders<ExchangeOrderData>.Update.Set(o => o.Status, OrderStatus.Failed));
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
    }
}