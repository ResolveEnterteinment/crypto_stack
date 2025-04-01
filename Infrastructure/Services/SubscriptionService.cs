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
                            new CreateIndexOptions { Name = "UserId_1" })
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
                    _logger.LogWarning("Failed to get allocations for subscription {SubscriptionId}: {Error}", insertedId, assetsResult.ErrorMessage);
                    throw new KeyNotFoundException($"Failed to get allocations for subscription {insertedId}: {assetsResult.ErrorMessage}");
                }
                _logger.LogInformation("Successfully inserted subscription {SubscriptionId}", result.InsertedId);
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
                var updateFields = new Dictionary<string, object>();

                var requestFields = request
                    .GetType()
                    .GetProperties()
                    .ToDictionary(property => property.Name, property => property.GetValue(request));

                foreach (var field in requestFields)
                {
                    if (field.Value != null)
                    {
                        updateFields[field.Key] = field.Value;
                    }
                }

                var updateResult = await UpdateOneAsync(id, requestFields);
                if (updateResult == null || !updateResult.IsAcknowledged)
                {
                    throw new MongoException($"Failed to update subscription {id}");
                }
                return ResultWrapper<long>.Success(updateResult.ModifiedCount);
            }
            catch (Exception ex)
            {
                return ResultWrapper<long>.FromException(ex);
            }
        }
        public async Task<ResultWrapper<IEnumerable<AllocationDto>>> GetAllocationsAsync(Guid subscriptionId)
        {
            try
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
                        AssetName = asset.Name,
                        AssetTicker = asset.Ticker,
                        PercentAmount = allocation.PercentAmount
                    });
                }
                return ResultWrapper<IEnumerable<AllocationDto>>.Success(allocationDtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fetch subscription failed: {Message}", ex.Message);
                return ResultWrapper<IEnumerable<AllocationDto>>.FromException(ex);
            }
        }
        public async Task<ResultWrapper<IEnumerable<SubscriptionDto>>> GetAllByUserIdAsync(Guid userId)
        {
            try
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
                        Allocations = allocationDtos,
                        Interval = subscription.Interval,
                        Amount = subscription.Amount,
                        Currency = subscription.Currency,
                        CreatedAt = subscription.CreatedAt,
                        NextDueDate = subscription.NextDueDate,
                        TotalInvestments = subscription.TotalInvestments,
                    });
                }
                return ResultWrapper<IEnumerable<SubscriptionDto>>.Success(subscriptionDtos);
            }
            catch (Exception ex)
            {
                return ResultWrapper<IEnumerable<SubscriptionDto>>.FromException(ex);
            }

        }
        public async Task<UpdateResult> CancelAsync(Guid subscriptionId)
        {
            var updatedFields = new { IsCancelled = true };
            return await UpdateOneAsync(subscriptionId, updatedFields);
        }
        public async Task Handle(SubscriptionCreatedEvent notification, CancellationToken cancellationToken)
        {
            var subscription = notification.Subscription.Data as Stripe.Subscription;
            var subscriptionId = subscription.Metadata["subscriptionId"];

            await UpdateOneAsync(Guid.Parse(subscriptionId), new
            {
                PaymentProviderSubscriptionId = subscription.Id,
                NextDueDate = subscription.CurrentPeriodEnd,
            });
        }

        public async Task Handle(PaymentReceivedEvent notification, CancellationToken cancellationToken)
        {
            var payment = notification.Payment;
            var subscriptionId = payment.SubscriptionId;
            var subscription = await GetByIdAsync(subscriptionId);

            var invenstmentQuantity = payment.NetAmount;
            var totalInvestments = subscription.TotalInvestments;
            var newQuantity = totalInvestments + invenstmentQuantity;

            var providerSubscription = await _paymentService.Providers[payment.Provider].GetSubscriptionAsync(subscription.ProviderSubscriptionId);
            var nextDueDate = providerSubscription.NextDueDate;

            await UpdateOneAsync(subscriptionId, new
            {
                NextDueDate = nextDueDate,
                TotalInvestments = newQuantity,
            });
        }
    }
}