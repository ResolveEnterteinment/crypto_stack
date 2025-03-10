using Application.Contracts.Requests.Payment;
using Application.Interfaces;
using AspNetCore.Identity.MongoDbCore.Infrastructure;
using Domain.Constants;
using Domain.DTOs;
using Domain.Events;
using Domain.Models.Payment;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using StripeLibrary;

namespace Infrastructure.Services
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

        /// <summary>
        /// Processes a charge.updated event from Stripe, fetching PaymentIntent data, calculating fees, and storing payment details.
        /// </summary>
        /// <param name="charge">The Stripe Charge object from the webhook event.</param>
        /// <returns>A ResultWrapper containing the Guid of the processed payment event or an error.</returns>
        public async Task<ResultWrapper<Guid>> ProcessChargeUpdatedEventAsync(ChargeRequest charge)
        {
            try
            {
                #region Validate Charge
                if (charge == null)
                {
                    throw new ArgumentNullException(nameof(charge), "Charge object cannot be null.");
                }
                if (string.IsNullOrEmpty(charge.PaymentIntentId))
                {
                    _logger.LogWarning("Charge {ChargeId} missing PaymentIntentId", charge.Id);
                    throw new ArgumentException("Charge must have an associated PaymentIntentId.");
                }
                #endregion Validate Charge

                #region Fetch and Validate PaymentIntent
                var paymentIntent = await _stripeService.GetPaymentIntentAsync(charge.PaymentIntentId);

                if (paymentIntent == null)
                {
                    _logger.LogWarning("Failed to retrieve PaymentIntent {PaymentIntentId} for Charge {ChargeId}", charge.PaymentIntentId, charge.Id);
                    throw new InvalidOperationException("Could not retrieve associated PaymentIntent.");
                }

                var userId = paymentIntent.Metadata["userId"];
                var subscriptionId = paymentIntent.Metadata["subscriptionId"];
                if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(subscriptionId))
                {
                    _logger.LogWarning("Missing metadata in PaymentIntent {PaymentId}: UserId or SubscriptionId", paymentIntent.Id);
                    throw new ArgumentException("PaymentIntent metadata must include userId and subscriptionId.");
                }

                if (!Guid.TryParse(userId, out Guid parsedUserId) || !Guid.TryParse(subscriptionId, out Guid parsedSubscriptionId))
                {
                    _logger.LogWarning("Invalid metadata format in PaymentIntent {PaymentId}: UserId={UserId}, SubscriptionId={SubscriptionId}", paymentIntent.Id, userId, subscriptionId);
                    throw new ArgumentException("UserId and SubscriptionId must be valid Guids.");
                }
                #endregion Fetch and Validate PaymentIntent

                #region Calculate Amounts
                var totalAmount = charge.Amount / 100m; // Convert cents to decimal
                var paymentFee = await _stripeService.GetStripeFeeAsync(paymentIntent.Id);
                var netAmountAfterStripe = totalAmount - paymentFee;
                var platformFee = totalAmount * 0.01m; // 1% platform fee
                var netAmount = netAmountAfterStripe - platformFee;

                if (netAmount <= 0)
                {
                    _logger.LogWarning("Net amount for PaymentIntent {PaymentId} is invalid: {NetAmount}", paymentIntent.Id, netAmount);
                    throw new ArgumentOutOfRangeException(nameof(netAmount), "Net amount must be greater than zero.");
                }
                #endregion Calculate Amounts

                #region Construct PaymentRequest
                var paymentRequest = new PaymentIntentRequest
                {
                    UserId = Guid.Parse(userId),
                    SubscriptionId = subscriptionId,
                    PaymentId = paymentIntent.Id,
                    TotalAmount = totalAmount,
                    PaymentProviderFee = paymentFee,
                    PlatformFee = platformFee,
                    NetAmount = netAmount,
                    Currency = charge.Currency,
                    Status = paymentIntent.Status
                };
                #endregion Construct PaymentRequest

                return await ProcessPaymentIntentSucceededEvent(paymentRequest);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process charge.updated event for Charge {ChargeId}", charge.Id);
                return ResultWrapper<Guid>.Failure(FailureReason.From(ex), ex.Message);
            }
        }

        /// <summary>
        /// Processes a payment request, storing it atomically with an event and publishing a PaymentReceivedEvent.
        /// </summary>
        /// <param name="paymentRequest">The payment details to process.</param>
        /// <returns>A ResultWrapper containing the Guid of the stored event or an error.</returns>
        public async Task<ResultWrapper<Guid>> ProcessPaymentIntentSucceededEvent(PaymentIntentRequest paymentRequest)
        {
            try
            {
                #region Validate PaymentRequest
                if (paymentRequest == null)
                {
                    throw new ArgumentNullException(nameof(paymentRequest), "Payment request cannot be null.");
                }
                if (string.IsNullOrWhiteSpace(paymentRequest.UserId.ToString()))
                {
                    throw new ArgumentException($"Invalid UserId: {paymentRequest.UserId}");
                }
                if (string.IsNullOrWhiteSpace(paymentRequest.SubscriptionId) || !Guid.TryParse(paymentRequest.SubscriptionId, out Guid subscriptionId))
                {
                    throw new ArgumentException($"Invalid SubscriptionId: {paymentRequest.SubscriptionId}");
                }
                if (string.IsNullOrWhiteSpace(paymentRequest.PaymentId))
                {
                    throw new ArgumentException($"Invalid PaymentProviderId: {paymentRequest.PaymentId}");
                }
                if (string.IsNullOrWhiteSpace(paymentRequest.Currency))
                {
                    throw new ArgumentException($"Invalid Currency: {paymentRequest.Currency}");
                }
                var assetResult = await _assetService.GetByTickerAsync(paymentRequest.Currency);
                if (!assetResult.IsSuccess)
                {
                    throw new ArgumentException($"Unable to fetch asset data from currency: {assetResult.ErrorMessage}");
                }
                if (paymentRequest.NetAmount <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(paymentRequest.NetAmount), "Amount must be greater than zero.");
                }
                #endregion Validate PaymentRequest

                #region Idempotency Check
                var existingPayment = await _collection.Find(p => p.PaymentProviderId == paymentRequest.PaymentId).FirstOrDefaultAsync();
                if (existingPayment != null)
                {
                    _logger.LogInformation("Payment {PaymentId} already processed.", paymentRequest.PaymentId);
                    return ResultWrapper<Guid>.Success(existingPayment.Id);
                }
                #endregion Idempotency Check

                var paymentData = new PaymentData
                {
                    UserId = paymentRequest.UserId,
                    SubscriptionId = subscriptionId,
                    PaymentProviderId = paymentRequest.PaymentId,
                    TotalAmount = paymentRequest.TotalAmount,
                    PaymentProviderFee = paymentRequest.PaymentProviderFee,
                    PlatformFee = paymentRequest.PlatformFee,
                    NetAmount = paymentRequest.NetAmount,
                    Status = paymentRequest.Status,
                };

                InsertResult storedEventResult = default;
                #region Atomic Transaction
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
                        var storedEvent = new Domain.Models.Event.EventData
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
                        throw;
                    }
                }
                #endregion Atomic Transaction

                // Publish event after commit
                await _eventService.Publish(new PaymentReceivedEvent(paymentData, storedEventResult.InsertedId.Value));
                return ResultWrapper<Guid>.Success(storedEventResult.InsertedId.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process payment request: {Message}", ex.Message);
                return ResultWrapper<Guid>.Failure(FailureReason.From(ex), ex.Message);
            }
        }

        /// <summary>
        /// Retrieves the Stripe fee for a given PaymentIntent ID.
        /// </summary>
        /// <param name="paymentId">The PaymentIntent ID to fetch the fee for.</param>
        /// <returns>The Stripe fee in decimal format.</returns>
        public async Task<decimal> GetFeeAsync(string paymentId)
        {
            return await _stripeService.GetStripeFeeAsync(paymentId);
        }
    }
}