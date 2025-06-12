using Application.Contracts.Requests.Subscription;
using Application.Interfaces;
using Application.Interfaces.Asset;
using Application.Interfaces.Base;
using Application.Interfaces.Logging;
using Application.Interfaces.Payment;
using Application.Interfaces.Subscription;
using Domain.Constants;
using Domain.Constants.Logging;
using Domain.DTOs;
using Domain.DTOs.Subscription;
using Domain.Events;
using Domain.Exceptions;
using Domain.Models.Subscription;
using Infrastructure.Services.Base;
using MongoDB.Driver;
using MongoDB.Driver.Linq;

namespace Infrastructure.Services.Subscription
{
    public class SubscriptionService :
        BaseService<SubscriptionData>,
        ISubscriptionService
    {
        private readonly IPaymentService _paymentService;
        private readonly IAssetService _assetService;
        private readonly INotificationService _notificationService;

        private const string CACHE_KEY_USER_SUBSCRIPTIONS = "user_subscriptions:{0}";
        private const string CACHE_KEY_SUBSCRIPTION_ALLOCATIONS = "subscription_allocations:{0}";

        public SubscriptionService(
            ICrudRepository<SubscriptionData> repository,
            ICacheService<SubscriptionData> cacheService,
            IMongoIndexService<SubscriptionData> indexService,
            ILoggingService logger,
            IPaymentService paymentService,
            IAssetService assetService,
            INotificationService notificationService,
            IEventService eventService
        ) : base(
            repository,
            cacheService,
            indexService,
            logger,
            eventService,
            new[]
            {
                new CreateIndexModel<SubscriptionData>(
                    Builders<SubscriptionData>.IndexKeys.Ascending(x => x.UserId),
                    new CreateIndexOptions { Name = "UserId_1" }
                ),
                new CreateIndexModel<SubscriptionData>(
                    Builders<SubscriptionData>.IndexKeys.Ascending(x => x.ProviderSubscriptionId),
                    new CreateIndexOptions { Name = "ProviderSubscriptionId_1", Sparse = true }
                )
            }
        )
        {
            _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
            _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        }

        public async Task<ResultWrapper<CrudResult>> CreateAsync(SubscriptionCreateRequest request)
        {
            try
            {
                // Validation
                if (request == null)
                    throw new ArgumentNullException(nameof(request));
                if (!Guid.TryParse(request.UserId, out var userId))
                    throw new ArgumentException($"Invalid UserId: {request.UserId}");
                if (request.Allocations == null || request.Allocations.Count() == 0)
                    throw new ArgumentException("At least one allocation is required.");
                if (string.IsNullOrWhiteSpace(request.Interval))
                    throw new ArgumentNullException(nameof(request.Interval));
                if (request.Amount <= 0)
                    throw new ArgumentOutOfRangeException(nameof(request.Amount), "Amount must be greater than zero.");

                // Build allocations
                var allocations = new List<AllocationData>();
                foreach (var alloc in request.Allocations)
                {
                    if (!Guid.TryParse(alloc.AssetId, out var assetId))
                        throw new ArgumentException($"Invalid AssetId: {alloc.AssetId}");
                    if (alloc.PercentAmount <= 0 || alloc.PercentAmount > 100)
                        throw new ArgumentOutOfRangeException(nameof(alloc.PercentAmount), "PercentAmount must be between 1 and 100.");

                    var assetWr = await _assetService.GetByIdAsync(assetId);
                    if (!assetWr.IsSuccess || assetWr.Data == null)
                        throw new AssetFetchException($"Failed to fetch asset ID {alloc.AssetId}");

                    allocations.Add(new AllocationData
                    {
                        AssetId = assetId,
                        Ticker = assetWr.Data.Ticker,
                        PercentAmount = alloc.PercentAmount
                    });
                }

                // Create subscription entity
                var subscriptionData = new SubscriptionData
                {
                    UserId = userId,
                    Allocations = allocations,
                    Interval = request.Interval,
                    Amount = request.Amount,
                    Currency = request.Currency,
                    NextDueDate = DateTime.UtcNow,
                    EndDate = request.EndDate,
                    Status = SubscriptionStatus.Pending
                };

                // Persist
                var insertResult = await InsertAsync(subscriptionData);
                if (insertResult == null || !insertResult.IsSuccess)
                    throw new MongoException("Failed to insert subscription into database.");

                // Notify user
                await _notificationService.CreateAndSendNotificationAsync(new NotificationData
                {
                    UserId = userId.ToString(),
                    Message = "A new subscription is created."
                });

                return ResultWrapper<CrudResult>.Success(insertResult.Data);
            }
            catch (Exception ex)
            {
                return ResultWrapper<CrudResult>.FromException(ex);
            }
        }

        public async Task<ResultWrapper> UpdateAsync(Guid id, SubscriptionUpdateRequest request)
        {
            try
            {
                // Validation
                if (request == null)
                    throw new ArgumentNullException(nameof(request));

                // Gather fields to update
                var updateFields = new Dictionary<string, object>();

                if (request.Allocations != null && request.Allocations.Count() > 0)
                {
                    var allocs = new List<AllocationData>();
                    foreach (var alloc in request.Allocations)
                    {
                        if (!Guid.TryParse(alloc.AssetId, out var assetId))
                            throw new ArgumentException($"Invalid AssetId: {alloc.AssetId}");
                        if (alloc.PercentAmount <= 0 || alloc.PercentAmount > 100)
                            throw new ArgumentOutOfRangeException(nameof(alloc.PercentAmount), "PercentAmount must be between 1 and 100.");

                        var assetWr = await _assetService.GetByIdAsync(assetId);
                        if (!assetWr.IsSuccess || assetWr.Data == null)
                            throw new AssetFetchException($"Failed to fetch asset ID {alloc.AssetId}");

                        allocs.Add(new AllocationData
                        {
                            AssetId = assetId,
                            Ticker = assetWr.Data.Ticker,
                            PercentAmount = alloc.PercentAmount
                        });
                    }
                    updateFields["Allocations"] = allocs;
                }
                if (!string.IsNullOrWhiteSpace(request.Interval))
                    updateFields["Interval"] = request.Interval;
                if (request.Amount.HasValue)
                    updateFields["Amount"] = request.Amount.Value;
                if (request.EndDate.HasValue)
                    updateFields["EndDate"] = request.EndDate.Value;

                // Execute update
                var result = await UpdateAsync(id, updateFields);

                return ResultWrapper.Success();
            }
            catch (Exception ex)
            {
                return ResultWrapper.FromException(ex);
            }
        }

        /// <summary>
        /// Updates the status of a subscription
        /// </summary>
        /// <param name="subscriptionId">The ID of the subscription</param>
        /// <param name="status">The new status</param>
        /// <returns>The result of the update operation</returns>
        public async Task<ResultWrapper<CrudResult>> UpdateSubscriptionStatusAsync(Guid subscriptionId, string status)
        {
            // Leverage BaseService.UpdateOneAsync
            var fields = new Dictionary<string, object> { ["Status"] = status };
            if (status == SubscriptionStatus.Canceled)
                fields["IsCancelled"] = true;

            var result = await UpdateAsync(subscriptionId, fields);
            if (result.IsSuccess)
            {
                var subWr = await GetByIdAsync(subscriptionId);

                var message = status switch
                {
                    SubscriptionStatus.Active => "activated",
                    SubscriptionStatus.Canceled => "cancelled",
                    SubscriptionStatus.Pending => "pending approval",
                    _ => "updated"
                };

                await _notificationService.CreateAndSendNotificationAsync(new NotificationData
                {
                    UserId = subWr.Data!.UserId.ToString(),
                    Message = $"Your subscription has been {message}."
                });
            }
            return result;
        }

        public async Task<ResultWrapper> CancelAsync(Guid subscriptionId)
        {
            var result = await UpdateSubscriptionStatusAsync(subscriptionId, SubscriptionStatus.Canceled);
            if (result == null || !result.IsSuccess)
                return ResultWrapper.Failure(result.Reason, result.ErrorMessage);
            return ResultWrapper.Success();
        }

        public Task<ResultWrapper<List<AllocationDto>>> GetAllocationsAsync(Guid subscriptionId)
            => SafeExecute(
                async () =>
                {
                    var subWr = await GetByIdAsync(subscriptionId);
                    if (!subWr.IsSuccess || subWr.Data == null)
                        throw new KeyNotFoundException($"Subscription #{subscriptionId} not found.");

                    var sub = subWr.Data;
                    if (sub.Allocations == null)
                        throw new ArgumentException($"No allocations for subscription #{subscriptionId}.");

                    var dtos = new List<AllocationDto>();
                    foreach (var a in sub.Allocations)
                    {
                        var assetWr = await _assetService.GetByIdAsync(a.AssetId);
                        dtos.Add(new AllocationDto
                        {
                            AssetId = a.AssetId,
                            AssetName = assetWr.Data!.Name,
                            AssetTicker = assetWr.Data.Ticker,
                            PercentAmount = a.PercentAmount
                        });
                    }
                    return dtos;
                }
            );

        public Task<ResultWrapper<List<SubscriptionDto>>> GetAllByUserIdAsync(Guid userId) =>
            SafeExecute(
                async () =>
                {
                    var filter = Builders<SubscriptionData>.Filter.Eq(x => x.UserId, userId);
                    var allWr = await _repository.GetAllAsync(filter);
                    if (allWr == null)
                    {
                        throw new SubscriptionFetchException($"Failed to fetch subscriptions for user {userId}");

                    }
                    var list = new List<SubscriptionDto>();

                    foreach (var sub in allWr)
                    {
                        var allocs = new List<AllocationDto>();
                        foreach (var a in sub.Allocations)
                        {
                            var assetWr = await _assetService.GetByIdAsync(a.AssetId);
                            allocs.Add(new AllocationDto
                            {
                                AssetId = a.AssetId,
                                AssetName = assetWr.Data.Name,
                                AssetTicker = assetWr.Data.Ticker,
                                PercentAmount = a.PercentAmount
                            });
                        }
                        list.Add(new SubscriptionDto
                        {
                            Id = sub.Id,
                            CreatedAt = sub.CreatedAt,
                            Allocations = allocs,
                            Interval = sub.Interval,
                            Amount = sub.Amount,
                            Currency = sub.Currency,
                            NextDueDate = sub.NextDueDate!.Value,
                            TotalInvestments = sub.TotalInvestments!.Value,
                            EndDate = sub.EndDate,
                            Status = sub.Status,
                            IsCancelled = sub.IsCancelled
                        });
                    }
                    return list;
                }
            );

        public async Task Handle(CheckoutSessionCompletedEvent notification, CancellationToken cancellationToken)
        {
            try
            {
                var subscriptionIdString = notification.Session.Metadata["subscriptionId"];
                if (!Guid.TryParse(subscriptionIdString, out var subscriptionId))
                {
                    throw new ArgumentException($"Invalid subscription ID format: {subscriptionIdString}");
                }

                Logger.LogInformation("Processing checkout.session.completed event for internal subscription {SubscriptionId} update...",
                    subscriptionId);

                // Update our subscription with the active status
                var updatedFields = new
                {
                    Provider = notification.Session.Provider,
                    ProviderSubscriptionId = notification.Session.SubscriptionId,
                    Status = SubscriptionStatus.Active
                };

                var updateResult = await UpdateAsync(subscriptionId, updatedFields);

                updateResult.OnSuccess(async (update) =>
                {
                    // Get the subscription to invalidate cache
                    var subscriptionResult = await GetByIdAsync(subscriptionId);
                    subscriptionResult.OnSuccess(async (subscription) =>
                    {
                        // Notify user
                        await _notificationService.CreateAndSendNotificationAsync(new()
                        {
                            UserId = subscription.UserId.ToString(),
                            Message = "Your subscription has been activated."
                        });


                        Logger.LogInformation("Successfully updated subscription {SubscriptionId} with session details",
                            update.AffectedIds.First());
                    })
                    .OnFailure((_errorMessage, failureReason) =>
                    {
                        Logger.LogWarning("Failed to fetch updated subscription {SubscriptionId}", subscriptionId);
                    });
                })
                .OnFailure((_errorMessage, failureReason) =>
                {
                    Logger.LogWarning("Failed to update subscription {SubscriptionId} with session details",
                    subscriptionId);
                });
            }
            catch (Exception ex)
            {
                Logger.LogError("Error handling subscription created event for event {EventId}: {Message}",
                    notification.EventId, ex.Message);
                // We don't rethrow here because we don't want to fail the event handling pipeline
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

                Logger.LogInformation("Processing subscription created event for internal subscription {SubscriptionId}",
                    subscriptionId);

                // Update our subscription with the provider's ID and status
                var updatedFields = new Dictionary<string, object>
                {
                    ["ProviderSubscriptionId"] = stripeSubscription.Id,
                    ["Status"] = SubscriptionStatus.Active,
                    ["NextDueDate"] = stripeSubscription.CurrentPeriodEnd
                };

                var updateResult = await UpdateAsync(parsedSubscriptionId, updatedFields, cancellationToken);

                updateResult.OnSuccess(async (_) =>
                {
                    // Get the subscription to invalidate cache
                    var subscriptionResult = await GetByIdAsync(parsedSubscriptionId);
                    subscriptionResult.OnSuccess(async (subscription) =>
                    {
                        Logger.LogInformation("Successfully updated subscription {SubscriptionId} with provider details",
                            parsedSubscriptionId);

                        // Notify user
                        await _notificationService.CreateAndSendNotificationAsync(new()
                        {
                            UserId = subscription.UserId.ToString(),
                            Message = "Your subscription has been activated."
                        });
                    })
                    .OnFailure((errorMessage, failureReason) =>
                    {
                        Logger.LogWarning("Failed to update subscription {SubscriptionId}", parsedSubscriptionId);
                    });
                })
                .OnFailure((errorMessage, failureReason) =>
                {
                    Logger.LogWarning("Failed to update subscription {SubscriptionId} with provider details",
                        parsedSubscriptionId);
                });
            }
            catch (Exception ex)
            {
                Logger.LogError("Error handling subscription created event for event {EventId}: {Message}",
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

                Logger.LogInformation("Processing payment for subscription {SubscriptionId}: {Amount} {Currency}",
                    subscriptionId, payment.NetAmount, payment.Currency);

                // Get current subscription details
                var subscriptionResult = await GetByIdAsync(subscriptionId);
                if (subscriptionResult == null || !subscriptionResult.IsSuccess)
                {
                    throw new KeyNotFoundException($"Subscription {subscriptionId} not found");
                }

                var subscription = subscriptionResult.Data;

                // Calculate new investment total
                var investmentQuantity = payment.NetAmount;
                var totalInvestments = subscription!.TotalInvestments;
                var newQuantity = totalInvestments + investmentQuantity;

                // Get next due date from payment provider
                var nextDueDate = await _paymentService.Providers["Stripe"].GetNextDueDate(notification.Payment.InvoiceId);

                if (nextDueDate == null)
                {
                    var interval = subscription.Interval;
                    nextDueDate = interval switch
                    {
                        SubscriptionInterval.Daily => DateTime.Now.AddDays(1),
                        SubscriptionInterval.Weekly => DateTime.Now.AddWeeks(1),
                        SubscriptionInterval.Monthly => DateTime.Now.AddMonths(1),
                        SubscriptionInterval.Yearly => DateTime.Now.AddYears(1),
                        _ => DateTime.Now.AddMonths(1),
                    };
                }

                // Update subscription
                var updatedFields = new Dictionary<string, object>
                {
                    ["NextDueDate"] = nextDueDate,
                    ["TotalInvestments"] = newQuantity,
                    ["Status"] = SubscriptionStatus.Active
                };

                var updateResult = await UpdateAsync(subscriptionId, updatedFields);

                if (updateResult.IsSuccess)
                {
                    // Notify user
                    await _notificationService.CreateAndSendNotificationAsync(new()
                    {
                        UserId = subscription.UserId.ToString(),
                        Message = $"Payment of {payment.NetAmount} {payment.Currency} processed for your subscription."
                    });

                    Logger.LogInformation("Successfully updated subscription {SubscriptionId} with payment info",
                        subscriptionId);
                }
                else
                {
                    Logger.LogWarning("Failed to update subscription {SubscriptionId} with payment details",
                        subscriptionId);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Error handling payment received event for payment {PaymentId}: {Message}",
                    notification.Payment.Id, ex.Message);
                // We don't rethrow here to avoid disrupting the event pipeline
            }
        }

        public async Task Handle(PaymentCancelledEvent notification, CancellationToken cancellationToken)
        {
            try
            {
                var payment = notification.Payment;
                var subscriptionId = payment.SubscriptionId;

                Logger.LogInformation("Processing payment for subscription {SubscriptionId}: {Amount} {Currency}",
                    subscriptionId, payment.NetAmount, payment.Currency);

                // Get current subscription details
                var subscriptionResult = await GetByIdAsync(subscriptionId);
                if (subscriptionResult == null || !subscriptionResult.IsSuccess)
                {
                    throw new KeyNotFoundException($"Subscription {subscriptionId} not found");
                }

                var subscription = subscriptionResult.Data;
                var updateResult = await UpdateSubscriptionStatusAsync(payment.SubscriptionId, SubscriptionStatus.Canceled);

                if (updateResult.IsSuccess)
                {
                    // Notify user
                    await _notificationService.CreateAndSendNotificationAsync(new()
                    {
                        UserId = subscription.UserId.ToString(),
                        Message = $"Subscription #{payment.Id} of {payment.NetAmount} {payment.Currency} is cancelled."
                    });

                    Logger.LogInformation("Successfully cancelled subscription {SubscriptionId}.",
                        subscriptionId);
                }
                else
                {
                    Logger.LogWarning("Failed to cancel subscription {SubscriptionId}.",
                        subscriptionId);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Error handling payment received event for payment {PaymentId}: {Message}",
                    notification.Payment.Id, ex.Message);
                // We don't rethrow here to avoid disrupting the event pipeline
            }
        }

        public async Task Handle(PaymentMethodUpdatedEvent notification, CancellationToken cancellationToken)
        {
            try
            {
                var subscriptionId = notification.SubscriptionId;

                // Get subscription
                var subscriptionResult = await GetByIdAsync(subscriptionId);
                if (!subscriptionResult.IsSuccess || subscriptionResult.Data == null)
                {
                    Logger.LogWarning("Subscription {SubscriptionId} not found", subscriptionId);
                    return;
                }

                var subscription = subscriptionResult.Data;

                // Check if subscription is suspended - if so, reactivate it
                if (subscription.Status == SubscriptionStatus.Suspended)
                {
                    await ReactivateSubscriptionAsync(subscriptionId);
                    Logger.LogInformation("Reactivated suspended subscription {SubscriptionId}", subscriptionId);
                }

                // Notify user of successful payment method update
                await _notificationService.CreateAndSendNotificationAsync(new NotificationData
                {
                    UserId = subscription.UserId.ToString(),
                    Message = "Your payment method has been successfully updated.",
                    IsRead = false
                });
            }
            catch (Exception ex)
            {
                Logger.LogError("Error handling payment method updated event: {ErrorMessage}", ex.Message);
                // Don't rethrow to avoid disrupting the event pipeline
            }
        }

        public async Task Handle(SubscriptionReactivationRequestedEvent notification, CancellationToken cancellationToken)
        {
            try
            {
                await ReactivateSubscriptionAsync(notification.SubscriptionId);
            }
            catch (Exception ex)
            {
                Logger.LogError("Error handling subscription reactivation request: {ErrorMessage}", ex.Message);
                // Don't rethrow to avoid disrupting the event pipeline
            }
        }

        public async Task<ResultWrapper> ReactivateSubscriptionAsync(Guid subscriptionId)
        {
            using var scope = Logger.BeginScope("SubscriptionService::ReactivateSubscription", new
            {
                SubscriptionId = subscriptionId
            });

            try
            {
                // Get subscription
                var subscriptionResult = await GetByIdAsync(subscriptionId);
                if (!subscriptionResult.IsSuccess || subscriptionResult.Data == null)
                    throw new ResourceNotFoundException("Subscription", subscriptionId.ToString());

                var subscription = subscriptionResult.Data;

                // Check if subscription is suspended
                if (subscription.Status != SubscriptionStatus.Suspended)
                    throw new InvalidOperationException($"Cannot reactivate subscription with status {subscription.Status}");

                // Update status to active
                var updateResult = await UpdateSubscriptionStatusAsync(subscriptionId, SubscriptionStatus.Active);
                if (!updateResult.IsSuccess)
                    throw new DatabaseException(updateResult.ErrorMessage);

                // Notify user
                await _notificationService.CreateAndSendNotificationAsync(new NotificationData
                {
                    UserId = subscription.UserId.ToString(),
                    Message = $"Your subscription has been reactivated.",
                    IsRead = false
                });

                return ResultWrapper.Success();
            }
            catch (Exception ex)
            {
                await Logger.LogTraceAsync($"Failed to reactivate subscription {subscriptionId}: {ex.Message}",
                    level: LogLevel.Critical,
                    requiresResolution: true);

                return ResultWrapper.FromException(ex);
            }
        }

        public async Task TestLog()
        {
            using (Logger.BeginScope(new Dictionary<string, object>
            {
                ["Level"] = "Service"
            }))
            {
                await Logger.LogTraceAsync("Started scope TestLog");
                await Logger.LogTraceAsync("Testing LogTrace system.", "TestLog", LogLevel.Error, true);
            }
        }
    }
}
