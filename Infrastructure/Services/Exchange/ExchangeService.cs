using Application.Interfaces;
using Application.Interfaces.Exchange;
using BinanceLibrary;
using Domain.DTOs;
using Domain.DTOs.Exchange;
using Domain.DTOs.Settings;
using Domain.Exceptions;
using Domain.Models.Exchange;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Polly;
using Polly.Retry;
using System.Diagnostics;

namespace Infrastructure.Services.Exchange
{
    /// <summary>
    /// Service for managing cryptocurrency exchange operations and interactions.
    /// </summary>
    public class ExchangeService : BaseService<ExchangeOrderData>, IExchangeService
    {
        private readonly Dictionary<string, IExchange> _exchanges = new Dictionary<string, IExchange>(StringComparer.OrdinalIgnoreCase);
        private readonly IOptions<ExchangeServiceSettings> _exchangeServiceSettings;
        private readonly IEventService _eventService;
        private readonly ISubscriptionService _subscriptionService;
        private readonly IBalanceService _balanceService;
        private readonly ITransactionService _transactionService;
        private readonly IAssetService _assetService;
        private readonly AsyncRetryPolicy _retryPolicy;

        /// <summary>
        /// Gets a dictionary of available exchange integrations.
        /// </summary>
        public IReadOnlyDictionary<string, IExchange> Exchanges => _exchanges;

        /// <summary>
        /// Gets the default exchange to use when none is specified.
        /// </summary>
        public IExchange DefaultExchange => _exchanges.Values.FirstOrDefault()
            ?? throw new InvalidOperationException("No exchanges are configured or available");

        /// <summary>
        /// The ID of the fiat asset used for platform operations.
        /// </summary>
        public readonly Guid FiatAssetId;

        /// <summary>
        /// Initializes a new instance of the <see cref="ExchangeService"/> class.
        /// </summary>
        public ExchangeService(
            IOptions<ExchangeServiceSettings> exchangeServiceSettings,
            IOptions<MongoDbSettings> mongoDbSettings,
            IMongoClient mongoClient,
            IEventService eventService,
            ISubscriptionService subscriptionService,
            IBalanceService balanceService,
            ITransactionService transactionService,
            IAssetService assetService,
            ILogger<ExchangeService> logger,
            IMemoryCache cache
            ) : base(
                mongoClient,
                mongoDbSettings,
                "exchange_orders",
                logger,
                cache
                )
        {
            _exchangeServiceSettings = exchangeServiceSettings ?? throw new ArgumentNullException(nameof(exchangeServiceSettings));
            _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
            _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
            _balanceService = balanceService ?? throw new ArgumentNullException(nameof(balanceService));
            _transactionService = transactionService ?? throw new ArgumentNullException(nameof(transactionService));
            _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));

            // Configure retry policy
            _retryPolicy = Policy
                .Handle<Exception>(ex => !(ex is ArgumentException || ex is ValidationException))
                .WaitAndRetryAsync(
                    3,
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    (ex, timeSpan, retryCount, context) =>
                    {
                        _logger.LogWarning(ex,
                            "Error executing exchange operation. Retrying {RetryCount}/3 after {RetryInterval}ms. Operation: {Operation}",
                            retryCount, timeSpan.TotalMilliseconds, context?["Operation"]);
                    });

            if (_exchangeServiceSettings.Value == null || _exchangeServiceSettings.Value.ExchangeSettings == null)
            {
                _logger.LogWarning("No exchange settings configured. Exchange functionality will be limited.");
                return;
            }

            // Initialize exchanges
            InitExchanges();

            // Parse FiatAssetId with proper error handling
            if (!string.IsNullOrEmpty(_exchangeServiceSettings.Value.PlatformFiatAssetId) &&
                Guid.TryParse(_exchangeServiceSettings.Value.PlatformFiatAssetId, out Guid fiatId))
            {
                FiatAssetId = fiatId;
            }
            else
            {
                _logger.LogError("Invalid or missing PlatformFiatAssetId in configuration. Using empty GUID as default.");
                FiatAssetId = Guid.Empty;
            }
        }

        /// <summary>
        /// Initializes exchange connections based on configuration.
        /// </summary>
        private void InitExchanges()
        {
            foreach (var (exchangeName, settings) in _exchangeServiceSettings.Value.ExchangeSettings)
            {
                try
                {
                    switch (exchangeName.ToLowerInvariant())
                    {
                        case "binance":
                            _exchanges.Add(exchangeName, new BinanceService(settings, _logger));
                            _logger.LogInformation("Successfully initialized exchange: {ExchangeName}", exchangeName);
                            break;
                        // Add support for other exchanges here
                        default:
                            _logger.LogWarning("Unsupported exchange '{ExchangeName}' found in configuration. Skipping.", exchangeName);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to initialize exchange '{ExchangeName}'. Skipping.", exchangeName);
                }
            }

            if (!_exchanges.Any())
            {
                _logger.LogWarning("No valid exchanges were initialized. Exchange functionality will be unavailable.");
            }
        }

        /// <summary>
        /// Starts a new MongoDB client session for transaction management.
        /// </summary>
        public async Task<IClientSessionHandle> StartDBSession(CancellationToken cancellationToken = default)
        {
            return await _mongoClient.StartSessionAsync(null, cancellationToken);
        }

        /// <summary>
        /// Retrieves all pending exchange orders that need reconciliation.
        /// </summary>
        public async Task<ResultWrapper<IEnumerable<ExchangeOrderData>>> GetPendingOrdersAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var activity = new Activity("ExchangeService.GetPendingOrdersAsync").Start();

                var filter = Builders<ExchangeOrderData>.Filter.Eq(o => o.Status, Domain.Constants.OrderStatus.Pending);
                var pendingOrders = await GetAllAsync(filter, cancellationToken);

                return ResultWrapper<IEnumerable<ExchangeOrderData>>.Success(pendingOrders);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve pending orders");
                return ResultWrapper<IEnumerable<ExchangeOrderData>>.FromException(ex);
            }
        }

        /// <summary>
        /// Creates a new exchange order.
        /// </summary>
        public async Task<ResultWrapper<ExchangeOrderData>> CreateOrderAsync(
            ExchangeOrderData order,
            IClientSessionHandle session = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var activity = new Activity("ExchangeService.CreateOrderAsync").Start();

                if (order == null)
                {
                    throw new ArgumentNullException(nameof(order));
                }

                // Set created timestamp if not already set
                if (order.CreatedAt == default)
                {
                    order.CreatedAt = DateTime.UtcNow;
                }

                // Generate ID if not provided
                if (order.Id == Guid.Empty)
                {
                    order.Id = Guid.NewGuid();
                }

                // Validate order data
                if (order.UserId == Guid.Empty)
                {
                    throw new ValidationException("User ID is required", new Dictionary<string, string[]>
                    {
                        ["UserId"] = new[] { "User ID cannot be empty" }
                    });
                }

                if (order.AssetId == Guid.Empty)
                {
                    throw new ValidationException("Asset ID is required", new Dictionary<string, string[]>
                    {
                        ["AssetId"] = new[] { "Asset ID cannot be empty" }
                    });
                }

                if (string.IsNullOrEmpty(order.PaymentProviderId))
                {
                    throw new ValidationException("Payment Provider ID is required", new Dictionary<string, string[]>
                    {
                        ["PaymentProviderId"] = new[] { "Payment Provider ID cannot be empty" }
                    });
                }

                if (order.QuoteQuantity <= 0)
                {
                    throw new ValidationException("Quote quantity must be positive", new Dictionary<string, string[]>
                    {
                        ["QuoteQuantity"] = new[] { "Quote quantity must be greater than zero" }
                    });
                }

                // Insert the order
                var insertResult = await InsertOneAsync(order, session, cancellationToken);

                if (!insertResult.IsAcknowledged || insertResult.InsertedId == null)
                {
                    throw new DatabaseException($"Failed to insert exchange order: {insertResult?.ErrorMessage ?? "Unknown error"}");
                }

                _logger.LogInformation("Created exchange order: {OrderId}", order.Id);

                return ResultWrapper<ExchangeOrderData>.Success(order);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create exchange order");
                return ResultWrapper<ExchangeOrderData>.FromException(ex);
            }
        }

        /// <summary>
        /// Updates the status of an exchange order.
        /// </summary>
        public async Task<ResultWrapper<bool>> UpdateOrderStatusAsync(
            Guid orderId,
            string? status,
            decimal? quantityFilled = null,
            decimal? price = null,
            IClientSessionHandle session = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var activity = new Activity("ExchangeService.UpdateOrderStatusAsync").Start();

                if (orderId == Guid.Empty)
                {
                    throw new ArgumentException("Order ID cannot be empty", nameof(orderId));
                }

                if (string.IsNullOrEmpty(status))
                {
                    throw new ArgumentException("Status cannot be empty", nameof(status));
                }

                // Build update definition with provided values
                var updateBuilder = Builders<ExchangeOrderData>.Update;
                var updates = new List<UpdateDefinition<ExchangeOrderData>>
                {
                    updateBuilder.Set(o => o.Status, status)
                };

                if (quantityFilled.HasValue)
                {
                    updates.Add(updateBuilder.Set(o => o.Quantity, quantityFilled.Value));
                }

                if (price.HasValue)
                {
                    updates.Add(updateBuilder.Set(o => o.Price, price.Value));
                }

                var updateDefinition = updateBuilder.Combine(updates);
                var filter = Builders<ExchangeOrderData>.Filter.Eq(o => o.Id, orderId);

                UpdateResult result;
                if (session != null)
                {
                    result = await _collection.UpdateOneAsync(session, filter, updateDefinition, cancellationToken: cancellationToken);
                }
                else
                {
                    result = await _collection.UpdateOneAsync(filter, updateDefinition, cancellationToken: cancellationToken);
                }

                if (!result.IsAcknowledged)
                {
                    throw new DatabaseException($"Failed to update order status for order ID: {orderId}");
                }

                if (result.MatchedCount == 0)
                {
                    return ResultWrapper<bool>.Failure(
                        Domain.Constants.FailureReason.ResourceNotFound,
                        $"Order with ID {orderId} not found");
                }

                _logger.LogInformation("Updated order {OrderId} status to {Status}", orderId, status);

                return ResultWrapper<bool>.Success(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update order status for order ID: {OrderId}", orderId);
                return ResultWrapper<bool>.FromException(ex);
            }
        }

        /// <summary>
        /// Gets exchange orders by various criteria.
        /// </summary>
        public async Task<ResultWrapper<Domain.DTOs.PaginatedResult<ExchangeOrderData>>> GetOrdersAsync(
            Guid? userId = null,
            Guid? subscriptionId = null,
            string status = null,
            Guid? assetId = null,
            int page = 1,
            int pageSize = 20,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var activity = new Activity("ExchangeService.GetOrdersAsync").Start();

                // Validate pagination parameters
                if (page < 1)
                {
                    page = 1;
                }

                if (pageSize < 1 || pageSize > 100)
                {
                    pageSize = 20;
                }

                // Build filter based on provided criteria
                var filterBuilder = Builders<ExchangeOrderData>.Filter;
                var filters = new List<FilterDefinition<ExchangeOrderData>>();

                if (userId.HasValue && userId != Guid.Empty)
                {
                    filters.Add(filterBuilder.Eq(o => o.UserId, userId.Value));
                }

                if (subscriptionId.HasValue && subscriptionId != Guid.Empty)
                {
                    filters.Add(filterBuilder.Eq(o => o.SubscriptionId, subscriptionId.Value));
                }

                if (!string.IsNullOrEmpty(status))
                {
                    filters.Add(filterBuilder.Eq(o => o.Status, status));
                }

                if (assetId.HasValue && assetId != Guid.Empty)
                {
                    filters.Add(filterBuilder.Eq(o => o.AssetId, assetId.Value));
                }

                // Combine filters
                FilterDefinition<ExchangeOrderData> filter = filterBuilder.Empty;
                if (filters.Any())
                {
                    filter = filterBuilder.And(filters);
                }

                // Get total count for pagination
                long totalCount = await _collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);

                // Apply pagination
                var options = new FindOptions<ExchangeOrderData>
                {
                    Skip = (page - 1) * pageSize,
                    Limit = pageSize,
                    Sort = Builders<ExchangeOrderData>.Sort.Descending(o => o.CreatedAt)
                };

                var cursor = await _collection.FindAsync(filter, options, cancellationToken);
                var orders = await cursor.ToListAsync(cancellationToken);

                var result = new Domain.DTOs.PaginatedResult<ExchangeOrderData>
                {
                    Page = page,
                    PageSize = pageSize,
                    TotalCount = totalCount,
                    Items = orders
                };

                return ResultWrapper<Domain.DTOs.PaginatedResult<ExchangeOrderData>>.Success(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve exchange orders");
                return ResultWrapper<Domain.DTOs.PaginatedResult<ExchangeOrderData>>.FromException(ex);
            }
        }

        /// <summary>
        /// Gets exchange orders by payment provider ID.
        /// </summary>
        public async Task<ResultWrapper<IEnumerable<ExchangeOrderData>>> GetOrdersByPaymentProviderIdAsync(
            string paymentProviderId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var activity = new Activity("ExchangeService.GetOrdersByPaymentProviderIdAsync").Start();

                if (string.IsNullOrEmpty(paymentProviderId))
                {
                    throw new ArgumentException("Payment provider ID cannot be empty", nameof(paymentProviderId));
                }

                var filter = Builders<ExchangeOrderData>.Filter.Eq(o => o.PaymentProviderId, paymentProviderId);
                var orders = await GetAllAsync(filter, cancellationToken);

                return ResultWrapper<IEnumerable<ExchangeOrderData>>.Success(orders);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve exchange orders for payment provider ID: {PaymentProviderId}", paymentProviderId);
                return ResultWrapper<IEnumerable<ExchangeOrderData>>.FromException(ex);
            }
        }

        /// <summary>
        /// Checks if an exchange is available and properly configured.
        /// </summary>
        public async Task<ResultWrapper<bool>> IsExchangeAvailableAsync(
            string exchangeName,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var activity = new Activity("ExchangeService.IsExchangeAvailableAsync").Start();

                // Check if exchange exists in our configuration
                if (string.IsNullOrEmpty(exchangeName) || !_exchanges.TryGetValue(exchangeName, out var exchange))
                {
                    return ResultWrapper<bool>.Success(false, $"Exchange '{exchangeName ?? "null"}' is not configured");
                }

                // Check if we can connect to the exchange API
                return await _retryPolicy.ExecuteAsync(async (ctx) =>
                {
                    try
                    {
                        // Try to get balances as a simple API check
                        var balanceResult = await exchange.GetBalancesAsync();

                        if (!balanceResult.IsSuccess)
                        {
                            return ResultWrapper<bool>.Success(
                                false,
                                $"Exchange '{exchangeName}' is configured but not responding correctly: {balanceResult.ErrorMessage}");
                        }

                        return ResultWrapper<bool>.Success(true);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error checking exchange availability: {ExchangeName}", exchangeName);
                        return ResultWrapper<bool>.Success(
                            false,
                            $"Exchange '{exchangeName}' is configured but not responding: {ex.Message}");
                    }
                }, new Dictionary<string, object> { ["Operation"] = $"HealthCheck_{exchangeName}" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check exchange availability: {ExchangeName}", exchangeName);
                return ResultWrapper<bool>.FromException(ex);
            }
        }


    }
}