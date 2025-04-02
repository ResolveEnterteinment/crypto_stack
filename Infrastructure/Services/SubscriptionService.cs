using Application.Contracts.Requests.Subscription;
using Application.Interfaces;
using Application.Interfaces.Payment;
using Domain.Constants;
using Domain.DTOs;
using Domain.DTOs.Settings;
using Domain.DTOs.Subscription;
using Domain.Events;
using Domain.Models.Subscription;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Infrastructure.Services
{
    public class SubscriptionService :
        BaseService<SubscriptionData>,
        ISubscriptionService,
        INotificationHandler<SubscriptionCreatedEvent>,
        INotificationHandler<PaymentReceivedEvent>
    {
        private readonly IPaymentService _paymentService;
        private readonly IAssetService _assetService;
        private readonly INotificationService _notificationService;

        private const string CACHE_KEY_USER_SUBSCRIPTIONS = "user_subscriptions:{0}";
        private const string CACHE_KEY_SUBSCRIPTION_ALLOCATIONS = "subscription_allocations:{0}";

        public SubscriptionService(
            IPaymentService paymentService,
            IAssetService assetService,
            IOptions<MongoDbSettings> mongoDbSettings,
            IMongoClient mongoClient,
            ILogger<SubscriptionService> logger,
            IMemoryCache cache,
            INotificationService notificationService
            )
            : base(
                  mongoClient,
                  mongoDbSettings,
                  "subscriptions",
                  logger,
                  cache,
                  new List<CreateIndexModel<SubscriptionData>>()
                    {
                        new (Builders<SubscriptionData>.IndexKeys.Ascending(x => x.UserId),
                            new CreateIndexOptions { Name = "UserId_1" }),
                        new (Builders<SubscriptionData>.IndexKeys.Ascending(x => x.ProviderSubscriptionId),
                            new CreateIndexOptions { Name = "ProviderSubscriptionId_1", Sparse = true })
                    }
                  )
        {
            _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
            _assetService = assetService ?? throw new ArgumentNullException(nameof(_assetService));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(_notificationService));
        }

        public async Task<ResultWrapper<Guid>> Create(SubscriptionCreateRequest request)
        {
            try
            {
                // Validate request
                #region Validate
                if (request == null)
                {
                    throw new ArgumentNullException("Subscription request cannot be null.");
                }

                if (string.IsNullOrWhiteSpace(request.UserId) || !Guid.TryParse(request.UserId, out Guid userId))
                {
                    throw new ArgumentException($"Invalid UserId: {request.UserId}");
                }

                if (request.Allocations == null || !request.Allocations.Any())
                {
                    throw new ArgumentException("At least one allocation is required.");
                }

                foreach (var alloc in request.Allocations)
                {
                    if (string.IsNullOrWhiteSpace(alloc.AssetId) || !Guid.TryParse(alloc.AssetId, out _))
                    {
                        throw new ArgumentException($"Invalid AssetId: {alloc.AssetId}");
                    }
                    if (alloc.PercentAmount > 100)
                    {
                        throw new ArgumentOutOfRangeException($"PercentAmount must be between 0 and 100: {alloc.PercentAmount}");
                    }
                }

                if (string.IsNullOrWhiteSpace(request.Interval))
                {
                    throw new ArgumentNullException("Interval is required.");
                }

                if (request.Amount <= 0)
                {
                    throw new ArgumentOutOfRangeException("Amount must be greater than zero.");
                }
                #endregion Validate

                List<AllocationData> allocations = [];
                foreach (var alloc in request.Allocations)
                {
                    var asset = await _assetService.GetByIdAsync(Guid.Parse(alloc.AssetId));
                    allocations.Add(new()
                    {
                        AssetId = Guid.Parse(alloc.AssetId),
                        Ticker = asset.Ticker,
                        PercentAmount = alloc.PercentAmount,
                    });
                };

                var subscriptionData = new SubscriptionData
                {
                    Provider = request.Provider,
                    UserId = userId,
                    Allocations = allocations,
                    Interval = request.Interval,
                    Amount = request.Amount,
                    Currency = request.Currency,
                    NextDueDate = DateTime.UtcNow,
                    EndDate = request.EndDate,
                    Status = SubscriptionStatus.Pending
                };

                var result = await InsertOneAsync(subscriptionData);
                if (!result.IsAcknowledged)
                {
                    throw new MongoException("Failed to insert subscription into database.");
                }

                var insertedId = result.InsertedId.Value;
                var assetsResult = await GetAllocationsAsync(insertedId);

                if (!assetsResult.IsSuccess)
                {
                    _logger.LogWarning("Failed to get allocations for subscription {SubscriptionId}: {Error}",
                        insertedId, assetsResult.ErrorMessage);
                    throw new KeyNotFoundException($"Failed to get allocations for subscription {insertedId}: {assetsResult.ErrorMessage}");
                }

                _logger.LogInformation("Successfully inserted subscription {SubscriptionId}", result.InsertedId);

                // Invalidate user subscriptions cache
                _cache.Remove(string.Format(CACHE_KEY_USER_SUBSCRIPTIONS, userId));

                await _notificationService.CreateNotificationAsync(new()
                {
                    UserId = userId.ToString(),
                    Message = "A new subscription is created."
                });

                return ResultWrapper<Guid>.Success(result.InsertedId.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to process subscription request: {ex.Message}");
                return ResultWrapper<Guid>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<long>> Update(Guid id, SubscriptionUpdateRequest request)
        {
            // Validate request
            #region Validate
            if (request == null)
            {
                throw new ArgumentNullException("Subscription update request cannot be null.");
            }

            if (request.Allocations != null)
            {
                if (request.Allocations.Count() == 0)
                {
                    throw new ArgumentException("Asset allocation can not be empty.");
                }

                foreach (var alloc in request.Allocations)
                {
                    if (string.IsNullOrWhiteSpace(alloc.AssetId) || !Guid.TryParse(alloc.AssetId, out _))
                    {
                        throw new ArgumentException($"Invalid AssetId: {alloc.AssetId}");
                    }
                    if (alloc.PercentAmount <= 0 || alloc.PercentAmount > 100)
                    {
                        throw new ArgumentOutOfRangeException($"PercentAmount must be between 0 and 100. Found: {alloc.PercentAmount}");
                    }
                }
                var percentTotal = request.Allocations.Select(a => (int)a.PercentAmount).Sum();
                if (percentTotal > 100)
                {
                    throw new ArgumentOutOfRangeException($"allocations percent total can not exceed 100. Found: {percentTotal}");
                }
            }

            if (!string.IsNullOrWhiteSpace(request.Interval) && !SubscriptionInterval.AllValues.Contains(request.Interval))
            {
                throw new ArgumentException($"Invalid interval. Interval must be of of {SubscriptionInterval.AllValues} Found: {request.Interval}");
            }

            if (request.Amount != null && request.Amount <= 0)
            {
                throw new ArgumentOutOfRangeException("Amount must be greater than zero.");
            }

            if (request.EndDate != null && request.EndDate <= DateTime.UtcNow)
            {
                throw new ArgumentOutOfRangeException("Invalid end date. End date can not before today's date.");
            }
            #endregion Validate

            try
            {
                // Get the subscription before updating to know the user ID for cache invalidation
                var subscription = await GetByIdAsync(id);
                if (subscription == null)
                {
                    throw new KeyNotFoundException($"Subscription with ID {id} not found");
                }

                var updateFields = new Dictionary<string, object>();

                // Only include non-null fields in the update
                if (request.Allocations != null && request.Allocations.Any())
                {
                    List<AllocationData> allocations = new();
                    foreach (var alloc in request.Allocations)
                    {
                        var asset = await _assetService.GetByIdAsync(Guid.Parse(alloc.AssetId));
                        allocations.Add(new()
                        {
                            AssetId = Guid.Parse(alloc.AssetId),
                            Ticker = asset.Ticker,
                            PercentAmount = alloc.PercentAmount,
                        });
                    }
                    updateFields["Allocations"] = allocations;
                }

                if (!string.IsNullOrEmpty(request.Interval))
                {
                    updateFields["Interval"] = request.Interval;
                }

                if (request.Amount.HasValue)
                {
                    updateFields["Amount"] = request.Amount.Value;
                }

                if (request.EndDate.HasValue)
                {
                    updateFields["EndDate"] = request.EndDate.Value;
                }

                var updateResult = await UpdateOneAsync(id, updateFields);

                if (updateResult.ModifiedCount > 0)
                {
                    // Invalidate caches
                    _cache.Remove(string.Format(CACHE_KEY_USER_SUBSCRIPTIONS, subscription.UserId));
                    _cache.Remove(string.Format(CACHE_KEY_SUBSCRIPTION_ALLOCATIONS, id));

                    _logger.LogInformation("Successfully updated subscription {SubscriptionId} for user {UserId}",
                        id, subscription.UserId);
                }

                return ResultWrapper<long>.Success(updateResult.ModifiedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update subscription {SubscriptionId}: {Message}", id, ex.Message);
                return ResultWrapper<long>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<IEnumerable<AllocationDto>>> GetAllocationsAsync(Guid subscriptionId)
        {
            try
            {
                string cacheKey = string.Format(CACHE_KEY_SUBSCRIPTION_ALLOCATIONS, subscriptionId);

                return await GetOrCreateCachedItemAsync<ResultWrapper<IEnumerable<AllocationDto>>>(
                    cacheKey,
                    async () =>
                    {
                        var subscription = await GetByIdAsync(subscriptionId);
                        if (subscription == null)
                        {
                            throw new KeyNotFoundException($"Subscription #{subscriptionId} not found.");
                        }

                        if (subscription.Allocations == null || !subscription.Allocations.Any())
                        {
                            throw new ArgumentException($"Asset allocation fetch error. No allocation(s) found for subscription #{subscriptionId}.");
                        }

                        var allocationDtos = new List<AllocationDto>();
                        foreach (var allocation in subscription.Allocations)
                        {
                            var asset = await _assetService.GetByIdAsync(allocation.AssetId);
                            allocationDtos.Add(new AllocationDto
                            {
                                AssetId = asset.Id,
                                AssetName = asset.Name,
                                AssetTicker = asset.Ticker,
                                PercentAmount = allocation.PercentAmount
                            });
                        }

                        return ResultWrapper<IEnumerable<AllocationDto>>.Success(allocationDtos);
                    },
                    TimeSpan.FromMinutes(10)
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fetch subscription allocations failed for {SubscriptionId}: {Message}",
                    subscriptionId, ex.Message);
                return ResultWrapper<IEnumerable<AllocationDto>>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<IEnumerable<SubscriptionDto>>> GetAllByUserIdAsync(Guid userId)
        {
            try
            {
                string cacheKey = string.Format(CACHE_KEY_USER_SUBSCRIPTIONS, userId);

                return await GetOrCreateCachedItemAsync<ResultWrapper<IEnumerable<SubscriptionDto>>>(
                    cacheKey,
                    async () =>
                    {
                        var filter = Builders<SubscriptionData>.Filter.Eq(doc => doc.UserId, userId);
                        var subscriptions = await GetAllAsync(filter);
                        var subscriptionDtos = new List<SubscriptionDto>();

                        foreach (var subscription in subscriptions)
                        {
                            var allocationDtos = new List<AllocationDto>();
                            foreach (var allocation in subscription.Allocations)
                            {
                                var asset = await _assetService.GetByIdAsync(allocation.AssetId);
                                allocationDtos.Add(new AllocationDto
                                {
                                    AssetId = asset.Id,
                                    AssetName = asset.Name,
                                    AssetTicker = asset.Ticker,
                                    PercentAmount = allocation.PercentAmount
                                });
                            }

                            subscriptionDtos.Add(new SubscriptionDto
                            {
                                Id = subscription.Id,
                                CreatedAt = subscription.CreatedAt,
                                Allocations = allocationDtos,
                                Interval = subscription.Interval,
                                Amount = subscription.Amount,
                                Currency = subscription.Currency,
                                NextDueDate = subscription.NextDueDate,
                                TotalInvestments = subscription.TotalInvestments,
                                EndDate = subscription.EndDate,
                                Status = subscription.Status,
                                IsCancelled = subscription.IsCancelled
                            });
                        }

                        return ResultWrapper<IEnumerable<SubscriptionDto>>.Success(subscriptionDtos);
                    },
                    TimeSpan.FromMinutes(5)
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get subscriptions for user {UserId}: {Message}", userId, ex.Message);
                return ResultWrapper<IEnumerable<SubscriptionDto>>.FromException(ex);
            }
        }

        public async Task<UpdateResult> CancelAsync(Guid subscriptionId)
        {
            try
            {
                // Get subscription first to invalidate cache
                var subscription = await GetByIdAsync(subscriptionId);
                if (subscription == null)
                {
                    throw new KeyNotFoundException($"Subscription with ID {subscriptionId} not found");
                }

                var updatedFields = new { IsCancelled = true, Status = SubscriptionStatus.Cancelled };
                var result = await UpdateOneAsync(subscriptionId, updatedFields);

                if (result.ModifiedCount > 0)
                {
                    // Invalidate caches
                    _cache.Remove(string.Format(CACHE_KEY_USER_SUBSCRIPTIONS, subscription.UserId));

                    // Notify user
                    await _notificationService.CreateNotificationAsync(new()
                    {
                        UserId = subscription.UserId.ToString(),
                        Message = $"Your subscription has been cancelled."
                    });

                    _logger.LogInformation("Subscription {SubscriptionId} cancelled successfully for user {UserId}",
                        subscriptionId, subscription.UserId);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cancel subscription {SubscriptionId}: {Message}",
                    subscriptionId, ex.Message);
                throw;
            }
        }

        public async Task Handle(SubscriptionCreatedEvent notification, CancellationToken cancellationToken)
        {
            try
            {
                // Stripe subscription contains the metadata with our internal subscription ID
                // (This assumes your webhook implementation in PaymentWebhookController correctly parses this)
                var stripeSubscription = notification.Subscription.Data as Stripe.Subscription;
                if (stripeSubscription?.Metadata == null ||
                    !stripeSubscription.Metadata.TryGetValue("subscriptionId", out var subscriptionId) ||
                    string.IsNullOrEmpty(subscriptionId))
                {
                    throw new ArgumentException("Invalid subscription data: missing subscription ID in metadata");
                }

                if (!Guid.TryParse(subscriptionId, out var parsedSubscriptionId))
                {
                    throw new ArgumentException($"Invalid subscription ID format: {subscriptionId}");
                }

                _logger.LogInformation("Processing subscription created event for internal subscription {SubscriptionId}",
                    subscriptionId);

                // Update our subscription with the provider's ID and status
                var updatedFields = new Dictionary<string, object>
                {
                    ["ProviderSubscriptionId"] = stripeSubscription.Id,
                    ["Status"] = SubscriptionStatus.Active,
                    ["NextDueDate"] = stripeSubscription.CurrentPeriodEnd
                };

                var updateResult = await UpdateOneAsync(parsedSubscriptionId, updatedFields);

                if (updateResult.ModifiedCount > 0)
                {
                    // Get the subscription to invalidate cache
                    var subscription = await GetByIdAsync(parsedSubscriptionId);
                    if (subscription != null)
                    {
                        _cache.Remove(string.Format(CACHE_KEY_USER_SUBSCRIPTIONS, subscription.UserId));

                        // Notify user
                        await _notificationService.CreateNotificationAsync(new()
                        {
                            UserId = subscription.UserId.ToString(),
                            Message = "Your subscription has been activated."
                        });
                    }

                    _logger.LogInformation("Successfully updated subscription {SubscriptionId} with provider details",
                        parsedSubscriptionId);
                }
                else
                {
                    _logger.LogWarning("Failed to update subscription {SubscriptionId} with provider details",
                        parsedSubscriptionId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling subscription created event for event {EventId}: {Message}",
                    notification.EventId, ex.Message);
                // We don't rethrow here because we don't want to fail the event handling pipeline
            }
        }

        public async Task Handle(PaymentReceivedEvent notification, CancellationToken cancellationToken)
        {
            try
            {
                var payment = notification.Payment;
                var subscriptionId = payment.SubscriptionId;

                _logger.LogInformation("Processing payment for subscription {SubscriptionId}: {Amount} {Currency}",
                    subscriptionId, payment.NetAmount, payment.Currency);

                // Get current subscription details
                var subscription = await GetByIdAsync(subscriptionId);
                if (subscription == null)
                {
                    throw new KeyNotFoundException($"Subscription {subscriptionId} not found");
                }

                // Calculate new investment total
                var investmentQuantity = payment.NetAmount;
                var totalInvestments = subscription.TotalInvestments;
                var newQuantity = totalInvestments + investmentQuantity;

                // Get next due date from payment provider
                DateTime nextDueDate;
                if (!string.IsNullOrEmpty(subscription.ProviderSubscriptionId))
                {
                    var providerSubscription = await _paymentService.Providers[payment.Provider]
                        .GetSubscriptionAsync(subscription.ProviderSubscriptionId);
                    nextDueDate = providerSubscription?.NextDueDate ?? DateTime.UtcNow.AddMonths(1);
                }
                else
                {
                    // Default to one month in the future if no provider subscription
                    nextDueDate = DateTime.UtcNow.AddMonths(1);
                }

                // Update subscription
                var updatedFields = new Dictionary<string, object>
                {
                    ["NextDueDate"] = nextDueDate,
                    ["TotalInvestments"] = newQuantity,
                    ["Status"] = SubscriptionStatus.Active
                };

                var updateResult = await UpdateOneAsync(subscriptionId, updatedFields);

                if (updateResult.ModifiedCount > 0)
                {
                    // Invalidate cache
                    _cache.Remove(string.Format(CACHE_KEY_USER_SUBSCRIPTIONS, subscription.UserId));

                    // Notify user
                    await _notificationService.CreateNotificationAsync(new()
                    {
                        UserId = subscription.UserId.ToString(),
                        Message = $"Payment of {payment.NetAmount} {payment.Currency} processed for your subscription."
                    });

                    _logger.LogInformation("Successfully updated subscription {SubscriptionId} with payment info",
                        subscriptionId);
                }
                else
                {
                    _logger.LogWarning("Failed to update subscription {SubscriptionId} with payment details",
                        subscriptionId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling payment received event for payment {PaymentId}: {Message}",
                    notification.Payment.Id, ex.Message);
                // We don't rethrow here to avoid disrupting the event pipeline
            }
        }
    }
}