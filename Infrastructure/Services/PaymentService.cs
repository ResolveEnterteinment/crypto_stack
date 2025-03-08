using Application.Contracts.Requests.Payment;
using Application.Interfaces;
using AspNetCore.Identity.MongoDbCore.Infrastructure;
using Domain.Constants;
using Domain.DTOs;
using Domain.Events;
using Domain.Modals.Event;
using Domain.Models.Payment;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using StripeLibrary;

namespace Domain.Services
{
    public class PaymentService : BaseService<PaymentData>, IPaymentService
    {
        private readonly IStripeService _stripeService;
        private readonly IAssetService _assetService;
        private readonly IEventService _eventService;


        public PaymentService(
            IOptions<StripeSettings> stripeSettings,
            IAssetService assetService,
            IOptions<MongoDbSettings> mongoDbSettings,
            IMongoClient mongoClient,
            ILogger<PaymentService> logger,
            IEventService eventService)
            : base(mongoClient, mongoDbSettings, "payments", logger)
        {
            _stripeService = new StripeService(stripeSettings, logger);
            _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
            _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
        }

        public async Task<ResultWrapper<ObjectId>> ProcessPaymentRequest(PaymentRequest request)
        {
            try
            {
                // Validate request
                #region Validate
                if (request == null)
                {
                    throw new ArgumentNullException("Subscription request cannot be null.");
                }
                if (string.IsNullOrWhiteSpace(request.UserId) || !ObjectId.TryParse(request.UserId, out ObjectId userId))
                {
                    throw new ArgumentException($"Invalid UserId: {request.UserId}");
                }
                if (string.IsNullOrWhiteSpace(request.SubscriptionId) || !ObjectId.TryParse(request.SubscriptionId, out ObjectId subscriptionId))
                {
                    throw new ArgumentException($"Invalid UserId: {request.UserId}");
                }
                if (string.IsNullOrWhiteSpace(request.PaymentId))
                {
                    throw new ArgumentException($"Invalid PaymentProviderId: {request.PaymentId}");
                }
                if (string.IsNullOrWhiteSpace(request.Currency))
                {
                    throw new ArgumentException($"Invalid Currency: {request.Currency}");
                }
                var assetResult = await _assetService.GetByTickerAsync(request.Currency);
                if (!assetResult.IsSuccess)
                {
                    throw new ArgumentException($"Unable to fetch asset data from currency: {assetResult.ErrorMessage}");
                }
                if (request.NetAmount <= 0)
                {
                    throw new ArgumentOutOfRangeException("Amount must be greater than zero.");
                }
                #endregion Validate

                var paymentData = new PaymentData
                {
                    UserId = userId,
                    SubscriptionId = subscriptionId,
                    PaymentProviderId = request.PaymentId,
                    TotalAmount = request.TotalAmount,
                    PaymentProviderFee = request.PaymentProviderFee,
                    PlatformFee = request.PlatformFee,
                    NetAmount = request.NetAmount,
                    Status = request.Status,
                };

                InsertResult storedEventResult = default;
                // Atomic insert of transaction and event
                using (var session = await _mongoClient.StartSessionAsync())
                {
                    session.StartTransaction();
                    try
                    {
                        var insertPaymentResult = await InsertOneAsync(paymentData, session);
                        if (!insertPaymentResult.IsAcknowledged || insertPaymentResult.InsertedId is null)
                        {
                            throw new MongoException(insertPaymentResult.ErrorMessage);
                        }
                        var storedEvent = new EventData
                        {
                            EventType = typeof(PaymentReceivedEvent).Name,
                            Payload = insertPaymentResult.InsertedId.ToString()
                        };
                        storedEventResult = await _eventService.InsertOneAsync(storedEvent, session);
                        if (!storedEventResult.IsAcknowledged || storedEventResult.InsertedId is null)
                        {
                            throw new MongoException(storedEventResult.ErrorMessage);
                        }
                        await session.CommitTransactionAsync();
                        _logger.LogInformation($"Successfully inserted payment {insertPaymentResult.InsertedId} and event {storedEventResult.InsertedId}");
                    }
                    catch (Exception ex)
                    {
                        await session.AbortTransactionAsync();
                        _logger.LogError(ex, "Failed to atomically insert transaction and event");
                        throw; // Trigger Stripe retry
                    }
                    // Publish event after commit
                    await _eventService.Publish(new PaymentReceivedEvent(paymentData, storedEventResult.InsertedId.AsObjectId));
                    return ResultWrapper<ObjectId>.Success(storedEventResult.InsertedId.AsObjectId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process payment request: {Message}", ex.Message);
                return ResultWrapper<ObjectId>.Failure(FailureReason.From(ex), ex.Message);
            }
        }
        public async Task<decimal> GetFeeAsync(string paymentId)
        {
            return await _stripeService.GetStripeFeeAsync(paymentId);
        }
    }
}