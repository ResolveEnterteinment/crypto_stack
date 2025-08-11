using Application.Contracts.Requests.Subscription;
using Application.Interfaces.Asset;
using Application.Interfaces.Payment;
using Application.Interfaces.Subscription;
using Domain.Constants;
using Domain.Constants.Logging;
using Domain.DTOs;
using Domain.DTOs.Logging;
using Domain.DTOs.Subscription;
using Domain.Events.Payment;
using Domain.Events.Subscription;
using Domain.Exceptions;
using Domain.Exceptions.Subscripiton;
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

        public SubscriptionService(
            IServiceProvider serviceProvider,
            IPaymentService paymentService,
            IAssetService assetService,
            INotificationService notificationService
        ) : base(
            serviceProvider,
            new()
            {
                IndexModels = [
                    new CreateIndexModel<SubscriptionData>(
                        Builders<SubscriptionData>.IndexKeys.Ascending(x => x.UserId),
                        new CreateIndexOptions { Name = "UserId_1" }
                    ),
                    new CreateIndexModel<SubscriptionData>(
                        Builders<SubscriptionData>.IndexKeys.Ascending(x => x.ProviderSubscriptionId),
                        new CreateIndexOptions { Name = "ProviderSubscriptionId_1", Sparse = true }
                    )]
            })
        {
            _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
            _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        }

        public async Task<ResultWrapper<CrudResult<SubscriptionData>>> CreateAsync(SubscriptionCreateRequest request)
        {
            // Validation
            if (request == null)
                return ResultWrapper<CrudResult<SubscriptionData>>.FromException(new ArgumentNullException(nameof(request)));
            
            if (!Guid.TryParse(request.UserId, out var userId))
                return ResultWrapper<CrudResult<SubscriptionData>>.FromException(
                    new ArgumentException($"Invalid UserId format: {request.UserId}"));
            
            if (request.Allocations == null || request.Allocations.Count() == 0)
                return ResultWrapper<CrudResult<SubscriptionData>>.FromException(
                    new ArgumentException("At least one allocation is required."));
            
            if (string.IsNullOrWhiteSpace(request.Interval))
                return ResultWrapper<CrudResult<SubscriptionData>>.FromException(
                    new ArgumentException("Interval is required."));
            
            if (request.Amount <= 0)
                return ResultWrapper<CrudResult<SubscriptionData>>.FromException(
                    new ArgumentOutOfRangeException(nameof(request.Amount), "Amount must be greater than zero."));

            return await _resilienceService.CreateBuilder(
               new Scope
               {
                   NameSpace = "Infrastructure.Services.Subscription",
                   FileName = "SubscriptionService",
                   OperationName = "CreateAsync(SubscriptionCreateRequest request)",
                   State = {
                       ["UserId"] = request.UserId,
                       ["Interval"] = request.Interval,
                       ["Amount"] = request.Amount,
                       ["Currency"] = request.Currency,
                   },
                   LogLevel = LogLevel.Error
               },
               async () =>
               {
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

                   return insertResult.Data;
               })
                .OnSuccess(async (result) =>
                {
                    // Notify user of successful creation
                    await _notificationService.CreateAndSendNotificationAsync(new NotificationData
                    {
                        UserId = request.UserId,
                        Message = "Your subscription has been created successfully."
                    });
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<CrudResult<SubscriptionData>>> UpdateAsync(Guid id, SubscriptionUpdateRequest request)
        {
            return await _resilienceService.CreateBuilder(
               new Scope
               {
                   NameSpace = "Infrastructure.Services.Subscription",
                   FileName = "SubscriptionService",
                   OperationName = "UpdateAsync(Guid id, SubscriptionUpdateRequest request)",
                   State = {
                       ["SubscriptionId"] = id,
                   },
                   LogLevel = LogLevel.Error
               },
               async () =>
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

                   if (request.Amount.HasValue && request.Amount > 0)
                       updateFields["Amount"] = request.Amount.Value;
                   if (request.EndDate.HasValue)
                       updateFields["EndDate"] = request.EndDate.Value;

                   // Execute update
                   var result = await UpdateAsync(id, updateFields);

                   if (result == null || !result.IsSuccess)
                   {
                       throw new DatabaseException($"Failed to update subscription {id}: {result?.ErrorMessage ?? "Unknown error"}");
                   }

                   return result.Data;
               })
                .ExecuteAsync();
        }

        /// <summary>
        /// Updates the status of a subscription
        /// </summary>
        /// <param name="subscriptionId">The ID of the subscription</param>
        /// <param name="status">The new status</param>
        /// <returns>The result of the update operation</returns>
        public async Task<ResultWrapper<CrudResult<SubscriptionData>>> UpdateSubscriptionStatusAsync(Guid subscriptionId, string status)
        {
            return await _resilienceService.CreateBuilder(
               new Scope
               {
                   NameSpace = "Infrastructure.Services.Subscription",
                   FileName = "SubscriptionService",
                   OperationName = "UpdateSubscriptionStatusAsync(Guid subscriptionId, string status)",
                   State = {
                       ["SubscriptionId"] = subscriptionId,
                       ["Status"] = status,
                   },
                   LogLevel = LogLevel.Error
               },
               async () =>
               {
                   // Leverage BaseService.UpdateOneAsync
                   var fields = new Dictionary<string, object> { ["Status"] = status };
                   if (status == SubscriptionStatus.Canceled)
                       fields["IsCancelled"] = true;

                   var result = await UpdateAsync(subscriptionId, fields);

                   return result.Data;
               })
                .OnSuccess(async (result) =>
                {
                    // Notify user of successful status update
                    var message = status switch
                    {
                        SubscriptionStatus.Active => "activated",
                        SubscriptionStatus.Canceled => "cancelled",
                        SubscriptionStatus.Pending => "pending approval",
                        _ => "updated"
                    };

                    await _notificationService.CreateAndSendNotificationAsync(new NotificationData
                    {
                        UserId = result.Documents.First().UserId.ToString(),
                        Message = $"Your subscription has been {message}."
                    });
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<CrudResult<SubscriptionData>>> CancelAsync(Guid subscriptionId)
        {
            return await _resilienceService.CreateBuilder(
               new Scope
               {
                   NameSpace = "Infrastructure.Services.Subscription",
                   FileName = "SubscriptionService",
                   OperationName = "CancelAsync(Guid subscriptionId)",
                   State = {
                        ["SubscriptionId"] = subscriptionId,
                   },
                   LogLevel = LogLevel.Error
               },
               async () =>
               {
                   // First, get the domain subscription record to verify it exists
                   var subscriptionResult = await GetByIdAsync(subscriptionId);
                   if (!subscriptionResult.IsSuccess || subscriptionResult.Data == null)
                       throw new KeyNotFoundException($"Subscription {subscriptionId} not found: {subscriptionResult?.ErrorMessage ?? "Subscription fetch returned null"}");

                   var subscription = subscriptionResult.Data;
                   string? stripeSubscriptionId = null;
                   bool stripeCancel­lationSuccessful = false;

                   _loggingService.LogInformation("Starting cancellation process for subscription {SubscriptionId} with status {Status}",
                       subscriptionId, subscription.Status);

                   // STEP 1: Get Stripe subscription ID from domain subscription record
                   if (!string.IsNullOrEmpty(subscription.ProviderSubscriptionId))
                   {
                       stripeSubscriptionId = subscription.ProviderSubscriptionId;
                       _loggingService.LogInformation("Found Stripe subscription ID {StripeSubscriptionId} in domain record",
                           stripeSubscriptionId);
                   }
                   else
                   {
                       // STEP 2: If domain subscription has no Stripe subscription record, search by metadata
                       _loggingService.LogInformation("No provider subscription ID found, searching Stripe by metadata for subscription {SubscriptionId}",
                           subscriptionId);

                       var searchResult = await _paymentService.SearchStripeSubscriptionByMetadataAsync("subscriptionId", subscriptionId.ToString());
                       if (searchResult == null || !searchResult.IsSuccess || string.IsNullOrEmpty(searchResult.Data))
                       {
                           throw new SubscriptionCancelException($"Stripe subscription not found in metadata search: {searchResult?.ErrorMessage ?? "Search result returned null"}");
                       }

                       stripeSubscriptionId = searchResult.Data;
                       _loggingService.LogInformation("Found Stripe subscription ID {StripeSubscriptionId} via metadata search",
                           stripeSubscriptionId);
                   }

                    _loggingService.LogInformation("Attempting to cancel Stripe subscription {StripeSubscriptionId} (PRIORITY OPERATION)",
                        stripeSubscriptionId);

                    var stripeCancelResult = await _paymentService.CancelStripeSubscriptionAsync(stripeSubscriptionId);

                    if (stripeCancelResult == null || !stripeCancelResult.IsSuccess)
                    {
                        throw new SubscriptionCancelException(
                            $"Failed to cancel subscription {subscriptionId}: {stripeCancelResult?.ErrorMessage ?? "Stripe cancel result returned null"}");
                    }

                   // STEP 4: If Stripe cancellation was successful update domain record status
                   _loggingService.LogInformation("Updating domain subscription {SubscriptionId} status to cancelled",
                       subscriptionId);

                   var result = await UpdateSubscriptionStatusAsync(subscriptionId, SubscriptionStatus.Canceled);
                   if (result == null || !result.IsSuccess)
                       throw new DatabaseException($"Failed to cancel subscription {subscriptionId}: {result?.ErrorMessage ?? "Unknown error"}");

                   _loggingService.LogInformation("✅ Successfully cancelled subscription {SubscriptionId}. Stripe cancellation: {StripeCancelled}",
                       subscriptionId, stripeCancellationSuccessful ? "SUCCESS" : "N/A (no Stripe subscription)");

                   return result.Data;
               })
                .WithHttpResilience() // For Stripe API calls
                .WithPerformanceMonitoring(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(45)) // Allow more time for Stripe operations
                .OnError(async (ex) =>
                {
                    _loggingService.LogError("❌ CRITICAL ERROR cancelling subscription {SubscriptionId}: {Error}. " +
                        "If this was a Stripe cancellation failure, user may still be charged!",
                        subscriptionId, ex.Message);
                })
                .ExecuteAsync();
        }

        /// <summary>
        /// Pauses a subscription on user request.
        /// </summary>
        /// <param name="subscriptionId"></param>
        /// <returns></returns>
        /// <exception cref="ResourceNotFoundException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="DatabaseException"></exception>
        public async Task<ResultWrapper> PauseAsync(Guid subscriptionId)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Subscription",
                    FileName = "SubscriptionService",
                    OperationName = "PauseAsync(Guid subscriptionId)",
                    State = {
                [       "SubscriptionId"] = subscriptionId
                    },
                    LogLevel = LogLevel.Critical
                },
                async () =>
                {
                    // Get subscription
                    var subscriptionResult = await GetByIdAsync(subscriptionId);
                    if (!subscriptionResult.IsSuccess || subscriptionResult.Data == null)
                        throw new ResourceNotFoundException("Subscription", subscriptionId.ToString());

                    var subscription = subscriptionResult.Data;

                    // Check if subscription can be paused
                    if (subscription.Status != SubscriptionStatus.Active)
                        throw new InvalidOperationException($"Cannot pause subscription with status {subscription.Status}");

                    // If there's a Stripe subscription, pause it first
                    if (!string.IsNullOrEmpty(subscription.ProviderSubscriptionId))
                    {
                        _loggingService.LogInformation("Pausing Stripe subscription {StripeSubscriptionId} for subscription {SubscriptionId}",
                            subscription.ProviderSubscriptionId, subscriptionId);

                        var stripePauseResult = await _paymentService.PauseStripeSubscriptionAsync(subscription.ProviderSubscriptionId);
                        if (!stripePauseResult.IsSuccess)
                        {
                            throw new DatabaseException($"Failed to pause Stripe subscription: {stripePauseResult.ErrorMessage}");
                        }
                    }

                    // Update local subscription status to paused
                    var updateResult = await UpdateSubscriptionStatusAsync(subscriptionId, SubscriptionStatus.Paused);
                    if (!updateResult.IsSuccess)
                        throw new DatabaseException(updateResult.ErrorMessage);

                    // Notify user
                    await _notificationService.CreateAndSendNotificationAsync(new NotificationData
                    {
                        UserId = subscription.UserId.ToString(),
                        Message = "Your subscription has been paused.",
                        IsRead = false
                    });

                    _loggingService.LogInformation("✅ Successfully paused subscription {SubscriptionId}", subscriptionId);
                })
                .ExecuteAsync();
        }

        /// <summary>
        /// Resumes a subscription on user request
        /// </summary>
        /// <param name="subscriptionId"></param>
        /// <returns></returns>
        /// <exception cref="ResourceNotFoundException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="DatabaseException"></exception>
        public async Task<ResultWrapper> ResumeAsync(Guid subscriptionId)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Subscription",
                    FileName = "SubscriptionService",
                    OperationName = "ResumeAsync(Guid subscriptionId)",
                    State = {
                        ["SubscriptionId"] = subscriptionId
                    },
                    LogLevel = LogLevel.Critical
                },
                async () =>
                {
                    // Get subscription
                    var subscriptionResult = await GetByIdAsync(subscriptionId);
                    if (!subscriptionResult.IsSuccess || subscriptionResult.Data == null)
                        throw new ResourceNotFoundException("Subscription", subscriptionId.ToString());

                    var subscription = subscriptionResult.Data;

                    // Check if subscription can be resumed
                    if (subscription.Status != SubscriptionStatus.Paused)
                        throw new InvalidOperationException($"Cannot resume subscription with status {subscription.Status}");

                    // If there's a Stripe subscription, resume it first
                    if (!string.IsNullOrEmpty(subscription.ProviderSubscriptionId))
                    {
                        _loggingService.LogInformation("Resuming Stripe subscription {StripeSubscriptionId} for subscription {SubscriptionId}",
                            subscription.ProviderSubscriptionId, subscriptionId);

                        var stripeResumeResult = await _paymentService.ResumeStripeSubscriptionAsync(subscription.ProviderSubscriptionId);
                        if (!stripeResumeResult.IsSuccess)
                        {
                            throw new DatabaseException($"Failed to resume Stripe subscription: {stripeResumeResult.ErrorMessage}");
                        }
                    }

                    // Update local subscription status to active
                    var updateResult = await UpdateSubscriptionStatusAsync(subscriptionId, SubscriptionStatus.Active);
                    if (!updateResult.IsSuccess)
                        throw new DatabaseException(updateResult.ErrorMessage);

                    // Notify user
                    await _notificationService.CreateAndSendNotificationAsync(new NotificationData
                    {
                        UserId = subscription.UserId.ToString(),
                        Message = "Your subscription has been resumed.",
                        IsRead = false
                    });

                    _loggingService.LogInformation("✅ Successfully resumed subscription {SubscriptionId}", subscriptionId);
                })
                .ExecuteAsync();
        }

        /// <summary>
        /// Pauses a subscription when a stripe subscription resumed event is triggered
        /// </summary>
        /// <param name="subscriptionId"></param>
        /// <returns></returns>
        /// <exception cref="ResourceNotFoundException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="DatabaseException"></exception>
        public async Task<ResultWrapper> OnPauseAsync(Guid subscriptionId)
        {
                       return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Subscription",
                    FileName = "SubscriptionService",
                    OperationName = "OnPauseAsync(Guid subscriptionId)",
                    State = {
                        ["SubscriptionId"] = subscriptionId
                    },
                    LogLevel = LogLevel.Critical
                },
                async () =>
                {
                    // Get subscription
                    var subscriptionResult = await GetByIdAsync(subscriptionId);

                    if (!subscriptionResult.IsSuccess || subscriptionResult.Data == null)
                        throw new ResourceNotFoundException("Subscription", subscriptionId.ToString());

                    var subscription = subscriptionResult.Data;

                    // Check if subscription is active
                    if (subscription.Status != SubscriptionStatus.Active)
                        throw new InvalidOperationException($"Cannot pause subscription with status {subscription.Status}");

                    // Update status to paused
                    var updateResult = await UpdateSubscriptionStatusAsync(subscriptionId, SubscriptionStatus.Paused);

                    if (!updateResult.IsSuccess)
                        throw new DatabaseException(updateResult.ErrorMessage);

                    // Notify user
                    await _notificationService.CreateAndSendNotificationAsync(new NotificationData
                    {
                        UserId = subscription.UserId.ToString(),
                        Message = "Your subscription has been paused.",
                        IsRead = false
                    });
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper> OnResumeAsync(Guid subscriptionId)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Subscription",
                    FileName = "SubscriptionService",
                    OperationName = "OnResumeAsync(Guid subscriptionId)",
                    State = {
                        ["SubscriptionId"] = subscriptionId
                    },
                    LogLevel = LogLevel.Critical
                },
                async () =>
                {
                    // Get subscription
                    var subscriptionResult = await GetByIdAsync(subscriptionId);

                    if (!subscriptionResult.IsSuccess || subscriptionResult.Data == null)
                        throw new ResourceNotFoundException("Subscription", subscriptionId.ToString());

                    var subscription = subscriptionResult.Data;

                    // Check if subscription is paused
                    if (subscription.Status != SubscriptionStatus.Paused)
                        throw new InvalidOperationException($"Cannot resume subscription with status {subscription.Status}");

                    // Update status to active
                    var updateResult = await UpdateSubscriptionStatusAsync(subscriptionId, SubscriptionStatus.Active);

                    if (!updateResult.IsSuccess)
                        throw new DatabaseException(updateResult.ErrorMessage);

                    // Notify user
                    await _notificationService.CreateAndSendNotificationAsync(new NotificationData
                    {
                        UserId = subscription.UserId.ToString(),
                        Message = "Your subscription has been resumed.",
                        IsRead = false
                    });
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper> ReactivateSubscriptionAsync(Guid subscriptionId)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Subscription",
                    FileName = "SubscriptionService",
                    OperationName = "ReactivateSubscriptionAsync(Guid subscriptionId)",
                    State = {
                        ["SubscriptionId"] = subscriptionId
                    },
                    LogLevel = LogLevel.Critical
                },
                async () =>
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
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<CrudResult<SubscriptionData>>> DeleteAsync(Guid subscriptionId)
        {
            return await _resilienceService.CreateBuilder(
              new Scope
              {
                  NameSpace = "Infrastructure.Services.Subscription",
                  FileName = "SubscriptionService",
                  OperationName = "DeleteAsync(Guid subscriptionId)",
                  State = {
                       ["SubscriptionId"] = subscriptionId,
                  },
                  LogLevel = LogLevel.Error
              },
              async () =>
              {
                  var result = await UpdateSubscriptionStatusAsync(subscriptionId, SubscriptionStatus.Deleted);
                  if (result == null || !result.IsSuccess)
                      throw new DatabaseException($"Failed to delete subscription {subscriptionId}: {result?.ErrorMessage ?? "Unknown error"}");
                  
                  return result.Data;
              })
                .ExecuteAsync();
        }

        public Task<ResultWrapper<List<AllocationDto>>> GetAllocationsAsync(Guid subscriptionId)
            => _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Subscription",
                    FileName = "SubscriptionService",
                    OperationName = "GetAllocationsAsync(Guid subscriptionId)",
                    State = {
                        ["subscriptionId"] = subscriptionId,
                    },
                    LogLevel = LogLevel.Error
                },
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
                })
            .ExecuteAsync();

        public Task<ResultWrapper<List<SubscriptionDto>>> GetAllByUserIdAsync(Guid userId, string? statusFilter = null)
             => _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Subscription",
                    FileName = "SubscriptionService",
                    OperationName = "GetAllByUserIdAsync(Guid userId, string? statusFilter = null)",
                    State = {
                        ["UserId"] = userId,
                        ["StatusFilter"] = statusFilter,
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    if (!string.IsNullOrWhiteSpace(statusFilter) && !SubscriptionStatus.AllValues.Contains(statusFilter.ToUpper()))
                    {
                        throw new ArgumentException($"Invalid ststus filter. Must be one of: {string.Join(",", SubscriptionStatus.AllValues)}");

                    }

                    FilterDefinition<SubscriptionData>[] filters = [
                        Builders<SubscriptionData>.Filter.Eq(x => x.UserId, userId),
                        Builders<SubscriptionData>.Filter.Not(Builders<SubscriptionData>.Filter.Eq(x => x.Status, SubscriptionStatus.Deleted)),
                        ];

                    if (!string.IsNullOrWhiteSpace(statusFilter))
                    {
                        filters.Append(Builders<SubscriptionData>.Filter.Eq(x => x.Status, statusFilter.ToUpper()));
                    }

                    var filter = Builders<SubscriptionData>.Filter.And(filters);

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
                        var lastPayment = sub.LastPayment;

                        if (sub.LastPayment == null)
                        {
                            var latestPayment = await _paymentService.GetLatestPaymentAsync(sub.Id);
                            lastPayment = latestPayment?.Data?.CreatedAt;
                        }
                        
                        list.Add(new SubscriptionDto
                        {
                            Id = sub.Id,
                            CreatedAt = sub.CreatedAt,
                            Allocations = allocs,
                            Interval = sub.Interval,
                            Amount = sub.Amount,
                            Currency = sub.Currency,
                            LastPayment = lastPayment,
                            NextDueDate = sub.NextDueDate!.Value,
                            TotalInvestments = sub.TotalInvestments!.Value,
                            EndDate = sub.EndDate,
                            Status = sub.Status,
                            IsCancelled = sub.IsCancelled
                        });
                    }
                    return list;
                })
            .ExecuteAsync();

        public async Task Handle(CheckoutSessionCompletedEvent notification, CancellationToken cancellationToken)
        {
            await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Subscription",
                    FileName = "SubscriptionService",
                    OperationName = "Handle(CheckoutSessionCompletedEvent notification, CancellationToken cancellationToken)",
                    State = {
                        ["EventType"] = typeof(CheckoutSessionCompletedEvent).Name,
                        ["EventId"] = notification.EventId,
                        ["SessionProvider"] = notification.Session.Provider,
                        ["SessionSubscriptionId"] = notification.Session.SubscriptionId
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    var subscriptionIdString = notification.Session.Metadata["subscriptionId"];
                    if (!Guid.TryParse(subscriptionIdString, out var subscriptionId))
                    {
                        throw new ArgumentException($"Invalid subscription ID format: {subscriptionIdString}");
                    }

                    _loggingService.LogInformation("Processing checkout.session.completed event for internal subscription {SubscriptionId} update...",
                        subscriptionId);

                    // Update our subscription with the active status
                    var updatedFields = new Dictionary<string, object>
                    {
                        ["Provider"] = notification.Session.Provider,
                        ["ProviderSubscriptionId"] = notification.Session.SubscriptionId,
                        ["Status"] = SubscriptionStatus.Active
                    };

                    var updateResult = await UpdateAsync(subscriptionId, updatedFields);

                    if (!updateResult.IsSuccess)
                    {
                        throw new DatabaseException($"Failed to update subscription {subscriptionId}: {updateResult.ErrorMessage}");
                    }
                    var userId = updateResult.Data.Documents.First().UserId;

                    await _notificationService.CreateAndSendNotificationAsync(new NotificationData
                    {
                        UserId = userId.ToString(),
                        Message = "Your subscription has been activated."
                    });

                    _loggingService.LogInformation("Successfully updated subscription {SubscriptionId} with session details",
                        subscriptionId);
                })
                .ExecuteAsync();
        }

        public async Task Handle(SubscriptionCreatedEvent notification, CancellationToken cancellationToken)
        {
            await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Subscription",
                    FileName = "SubscriptionService",
                    OperationName = "Handle(SubscriptionCreatedEvent notification, CancellationToken cancellationToken)",
                    State = {
                        ["EventType"] = typeof(SubscriptionCreatedEvent).Name,
                        ["EventId"] = notification.EventId,
                        ["StripeSubscriptionId"] = ((Stripe.Subscription)notification.Subscription.Data)?.Id,
                        ["InternalSubscriptionId"] = ((Stripe.Subscription)notification.Subscription.Data)?.Metadata?.GetValueOrDefault("subscriptionId")
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    // Stripe subscription contains the metadata with our internal subscription ID
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

                    _loggingService.LogInformation("Processing subscription created event for internal subscription {SubscriptionId}",
                        subscriptionId);

                    // Update our subscription with the provider's ID and status
                    var updatedFields = new Dictionary<string, object>
                    {
                        ["ProviderSubscriptionId"] = stripeSubscription.Id,
                        ["Status"] = SubscriptionStatus.Active,
                        ["NextDueDate"] = stripeSubscription.CurrentPeriodEnd
                    };

                    var updateResult = await UpdateAsync(parsedSubscriptionId, updatedFields, cancellationToken);

                    if (!updateResult.IsSuccess)
                    {
                        throw new DatabaseException($"Failed to update subscription {parsedSubscriptionId}: {updateResult.ErrorMessage}");
                    }

                    var userId = updateResult.Data.Documents.First().UserId;

                    // Get the subscription to send user notification
                    await _notificationService.CreateAndSendNotificationAsync(new NotificationData
                    {
                        UserId = userId.ToString(),
                        Message = "Your subscription has been activated."
                    });
                })
                .ExecuteAsync();
        }

        public async Task Handle(PaymentReceivedEvent notification, CancellationToken cancellationToken)
        {
            await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Subscription",
                    FileName = "SubscriptionService",
                    OperationName = "Handle(PaymentReceivedEvent notification, CancellationToken cancellationToken)",
                    State = {
                        ["EventType"] = typeof(PaymentReceivedEvent).Name,
                        ["PaymentProvider"] = notification.Payment.Provider ?? "Unknown",
                        ["PaymentId"] = notification.Payment.Id,
                        ["SubscriptionId"] = notification.Payment.SubscriptionId,
                        ["Amount"] = notification.Payment.NetAmount,
                        ["Currency"] = notification.Payment.Currency
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    var payment = notification.Payment;
                    var subscriptionId = payment.SubscriptionId;

                    _loggingService.LogInformation("Processing payment for subscription {SubscriptionId}: {Amount} {Currency}",
                        subscriptionId, payment.NetAmount, payment.Currency);

                    // Get current subscription details
                    var subscriptionResult = await GetByIdAsync(subscriptionId);
                    if (subscriptionResult == null || !subscriptionResult.IsSuccess)
                    {
                        throw new KeyNotFoundException($"Subscription {subscriptionId} not found: {subscriptionResult?.ErrorMessage ?? "Subscription fetch returned null"}");
                    }

                    var subscription = subscriptionResult.Data;

                    // Calculate new investment total
                    var subscriptionPaymentsResult = await _paymentService.GetPaymentsForSubscriptionAsync(payment.SubscriptionId);

                    if (subscriptionPaymentsResult == null || !subscriptionPaymentsResult.IsSuccess)
                    {
                        _loggingService.LogWarning("Failed to calculate investment totals for subscription {SubscriptionId}: {Error}",
                            subscriptionId, subscriptionPaymentsResult?.ErrorMessage ?? "Subscription payments returned null");
                    }

                    var totalInvestments = subscriptionPaymentsResult?.Data.Select(p => p.TotalAmount).Sum() ?? 0m;

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
                        ["LastPayment"] = payment.CreatedAt,
                        ["NextDueDate"] = nextDueDate,
                        ["TotalInvestments"] = totalInvestments,
                        ["Status"] = SubscriptionStatus.Active
                    };

                    var updateResult = await UpdateAsync(subscriptionId, updatedFields);

                    if (!updateResult.IsSuccess)
                    {
                        throw new DatabaseException($"Failed to update subscription {subscriptionId} with payment details: {updateResult.ErrorMessage}");
                    }

                    // Notify user
                    await _notificationService.CreateAndSendNotificationAsync(new NotificationData
                    {
                        UserId = subscription.UserId.ToString(),
                        Message = $"Payment of {payment.NetAmount} {payment.Currency} processed for your subscription."
                    });

                    _loggingService.LogInformation("Successfully updated subscription {SubscriptionId} with payment info",
                        subscriptionId);
                })
                .WithHttpResilience() // For notification service calls
                .WithPerformanceMonitoring(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)) // Monitor slow operations
                .OnError(async (ex) =>
                {
                    _loggingService.LogError("Critical error handling payment received event for payment {PaymentId}: {Error}",
                        notification.Payment.Id, ex.Message);
                })
                .ExecuteAsync();
        }
        public async Task Handle(PaymentCancelledEvent notification, CancellationToken cancellationToken)
        {
            await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Subscription",
                    FileName = "SubscriptionService",
                    OperationName = "Handle(PaymentCancelledEvent notification, CancellationToken cancellationToken)",
                    State = {
                        ["EventType"] = typeof(PaymentCancelledEvent).Name,
                        ["PaymentId"] = notification.Payment.Id,
                        ["SubscriptionId"] = notification.Payment.SubscriptionId,
                        ["Amount"] = notification.Payment.NetAmount,
                        ["Currency"] = notification.Payment.Currency,
                        ["PaymentProvider"] = notification.Payment.Provider ?? "Unknown"
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    var payment = notification.Payment;
                    var subscriptionId = payment.SubscriptionId;

                    _loggingService.LogInformation("Processing payment cancellation for subscription {SubscriptionId}: {Amount} {Currency}",
                        subscriptionId, payment.NetAmount, payment.Currency);

                    var updateResult = await UpdateSubscriptionStatusAsync(payment.SubscriptionId, SubscriptionStatus.Canceled);

                    if (!updateResult.IsSuccess || updateResult.Data.ModifiedCount == 0)
                    {
                        throw new DatabaseException($"Failed to cancel subscription {subscriptionId}: {updateResult.ErrorMessage}");
                    }

                    var subscription = updateResult.Data.Documents.FirstOrDefault();

                    // Notify user
                    await _notificationService.CreateAndSendNotificationAsync(new NotificationData
                    {
                        UserId = subscription.UserId.ToString(),
                        Message = $"Subscription #{payment.Id} of {payment.NetAmount} {payment.Currency} is cancelled."
                    });

                    _loggingService.LogInformation("Successfully cancelled subscription {SubscriptionId}.", subscriptionId);
                })
                .ExecuteAsync();
        }

        public async Task Handle(PaymentMethodUpdatedEvent notification, CancellationToken cancellationToken)
        {
            await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Subscription",
                    FileName = "SubscriptionService",
                    OperationName = "Handle(PaymentMethodUpdatedEvent notification, CancellationToken cancellationToken)",
                    State = {
                        ["EventType"] = typeof(PaymentMethodUpdatedEvent).Name,
                        ["SubscriptionId"] = notification.SubscriptionId,
                        ["UserId"] = notification.UserId
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    var subscriptionId = notification.SubscriptionId;

                    _loggingService.LogInformation("Processing payment method update for subscription {SubscriptionId}", subscriptionId);

                    // Get subscription
                    var subscriptionResult = await GetByIdAsync(subscriptionId);
                    if (!subscriptionResult.IsSuccess || subscriptionResult.Data == null)
                    {
                        throw new KeyNotFoundException($"Subscription {subscriptionId} not found: {subscriptionResult?.ErrorMessage ?? "Subscription fetch returned null"}");
                    }

                    var subscription = subscriptionResult.Data;

                    // Check if subscription is suspended - if so, reactivate it
                    if (subscription.Status == SubscriptionStatus.Suspended)
                    {
                        var reactivationResult = await ReactivateSubscriptionAsync(subscriptionId);
                        if (!reactivationResult.IsSuccess)
                        {
                            throw new DatabaseException($"Failed to reactivate subscription {subscriptionId}: {reactivationResult.ErrorMessage}");
                        }

                        _loggingService.LogInformation("Reactivated suspended subscription {SubscriptionId}", subscriptionId);
                    }

                    // Notify user of successful payment method update
                    await _notificationService.CreateAndSendNotificationAsync(new NotificationData
                    {
                        UserId = subscription.UserId.ToString(),
                        Message = "Your payment method has been successfully updated.",
                        IsRead = false
                    });
                })
                .ExecuteAsync();
        }

        public async Task Handle(SubscriptionReactivationRequestedEvent notification, CancellationToken cancellationToken)
        {
            await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Subscription",
                    FileName = "SubscriptionService",
                    OperationName = "Handle(SubscriptionReactivationRequestedEvent notification, CancellationToken cancellationToken)",
                    State = {
                        ["EventType"] = typeof(SubscriptionReactivationRequestedEvent).Name,
                        ["SubscriptionId"] = notification.SubscriptionId
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    _loggingService.LogInformation("Processing subscription reactivation request for subscription {SubscriptionId}",
                        notification.SubscriptionId);

                    var reactivationResult = await ReactivateSubscriptionAsync(notification.SubscriptionId);

                    if (!reactivationResult.IsSuccess)
                    {
                        throw new InvalidOperationException($"Failed to reactivate subscription {notification.SubscriptionId}: {reactivationResult.ErrorMessage}");
                    }

                    _loggingService.LogInformation("Successfully processed subscription reactivation request for subscription {SubscriptionId}",
                        notification.SubscriptionId);
                })
                .ExecuteAsync();
        }

    }
}
