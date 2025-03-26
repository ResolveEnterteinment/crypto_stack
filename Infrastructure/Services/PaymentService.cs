using Application.Contracts.Requests.Payment;
using Application.Interfaces;
using Application.Interfaces.Payment;
using Domain.Constants;
using Domain.DTOs;
using Domain.Events;
using Domain.Exceptions;
using Domain.Models.Payment;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Polly;
using StripeLibrary;
using System.Diagnostics;

namespace Infrastructure.Services
{
    public class PaymentService : BaseService<PaymentData>, IPaymentService
    {
        private readonly Dictionary<string, IPaymentProvider> _providers = new Dictionary<string, IPaymentProvider>(StringComparer.OrdinalIgnoreCase);
        private readonly IStripeService _stripeService;
        private readonly IAssetService _assetService;
        private readonly IEventService _eventService;
        private readonly INotificationService _notificationService;
        private readonly IIdempotencyService _idempotencyService;

        public Dictionary<string, IPaymentProvider> Providers { get => _providers; }

        public PaymentService(
            IOptions<StripeSettings> stripeSettings,
            IAssetService assetService,
            IOptions<MongoDbSettings> mongoDbSettings,
            IMongoClient mongoClient,
            ILogger<PaymentService> logger,
            IEventService eventService,
            INotificationService notificationService,
            IIdempotencyService idempotencyService
            )
            : base(mongoClient, mongoDbSettings, "payments", logger)
        {
            _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
            _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _idempotencyService = idempotencyService ?? throw new ArgumentNullException(nameof(idempotencyService));
            _stripeService = new StripeService(stripeSettings, logger);
            Providers.Add("Stripe", (IPaymentProvider)_stripeService);
        }

        /// <summary>
        /// Processes a charge.updated event from Stripe, fetching PaymentIntent data, calculating fees, and storing payment details.
        /// </summary>
        /// <param name="charge">The Stripe Charge object from the webhook event.</param>
        /// <returns>A ResultWrapper containing the Guid of the processed payment event or an error.</returns>
        public async Task<ResultWrapper<Guid>> ProcessChargeUpdatedEventAsync(ChargeRequest charge)
        {
            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["ChargeId"] = charge?.Id,
                ["PaymentIntentId"] = charge?.PaymentIntentId,
                ["Operation"] = "ProcessChargeUpdatedEvent",
                ["CorrelationId"] = Activity.Current?.Id
            }))
            {
                try
                {
                    // Define idempotency key from the charge ID
                    string idempotencyKey = $"charge-updated-{charge?.Id}";

                    // Check for existing processing
                    var (resultExists, existingResult) = await _idempotencyService.GetResultAsync<Guid>(idempotencyKey);
                    if (resultExists)
                    {
                        _logger.LogInformation("Charge {ChargeId} already processed with result {Result}", charge?.Id, existingResult);
                        return ResultWrapper<Guid>.Success(existingResult);
                    }

                    #region Validate Charge
                    if (charge == null)
                    {
                        throw new ValidationException("Charge object cannot be null", new Dictionary<string, string[]>
                        {
                            ["Charge"] = new[] { "Charge object cannot be null" }
                        });
                    }

                    if (string.IsNullOrEmpty(charge.PaymentIntentId))
                    {
                        _logger.LogWarning("Charge {ChargeId} missing PaymentIntentId", charge.Id);
                        throw new ValidationException("Missing PaymentIntentId", new Dictionary<string, string[]>
                        {
                            ["PaymentIntentId"] = new[] { "Charge must have an associated PaymentIntentId" }
                        });
                    }
                    #endregion Validate Charge

                    #region Fetch and Validate PaymentIntent
                    // Define a retry policy for external API calls
                    var apiRetryPolicy = Policy
                        .Handle<Exception>()
                        .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                            (ex, time, retryCount, context) =>
                            {
                                _logger.LogWarning(ex, "Retrying payment intent fetch (attempt {RetryCount}) after {RetryInterval}ms",
                                    retryCount, time.TotalMilliseconds);
                            });

                    var paymentIntent = await apiRetryPolicy.ExecuteAsync(
                        () => _stripeService.GetPaymentIntentAsync(charge.PaymentIntentId));

                    if (paymentIntent == null)
                    {
                        _logger.LogWarning("Failed to retrieve PaymentIntent {PaymentIntentId} for Charge {ChargeId}",
                            charge.PaymentIntentId, charge.Id);
                        throw new PaymentProcessingException(
                            $"Could not retrieve associated PaymentIntent {charge.PaymentIntentId}",
                            "Stripe",
                            charge.Id);
                    }

                    // Validate required metadata
                    var metadataValidationErrors = new List<string>();

                    if (!paymentIntent.Metadata.TryGetValue("userId", out var userId) ||
                        string.IsNullOrWhiteSpace(userId))
                    {
                        metadataValidationErrors.Add("PaymentIntent metadata must include valid userId.");
                    }
                    if (!paymentIntent.Metadata.TryGetValue("subscriptionId", out var subscriptionId) ||
                        string.IsNullOrWhiteSpace(userId))
                    {
                        metadataValidationErrors.Add("PaymentIntent metadata must include valid subscriptionId.");
                    }
                    if (metadataValidationErrors.Count > 0)
                    {
                        _logger.LogWarning("Missing or invalid metadata in PaymentIntent {PaymentId}: UserId={UserId}, SubscriptionId={SubscriptionId}",
                            paymentIntent.Id, userId, subscriptionId); //debug error
                        throw new ValidationException("Invalid metadata", new Dictionary<string, string[]>
                        {
                            ["Metadata"] = metadataValidationErrors.ToArray()
                        });
                    }

                    if (!Guid.TryParse(userId, out Guid parsedUserId) || !Guid.TryParse(subscriptionId, out Guid parsedSubscriptionId))
                    {
                        _logger.LogWarning("Invalid metadata format in PaymentIntent {PaymentId}: UserId={UserId}, SubscriptionId={SubscriptionId}",
                            paymentIntent.Id, userId, subscriptionId);
                        throw new ValidationException("Invalid metadata format", new Dictionary<string, string[]>
                        {
                            ["UserId"] = new[] { "UserId must be a valid Guid" },
                            ["SubscriptionId"] = new[] { "SubscriptionId must be a valid Guid" }
                        });
                    }
                    #endregion Fetch and Validate PaymentIntent

                    // Get next due date (could throw, will be caught by our catch block)
                    var nextDueDate = await Providers["Stripe"].GetNextDueDate(paymentIntent.InvoiceId);

                    #region Calculate Amounts
                    var totalAmount = charge.Amount / 100m; // Convert cents to decimal
                    var paymentFee = await Providers["Stripe"].GetFeeAsync(paymentIntent.Id);
                    var netAmountAfterStripe = totalAmount - paymentFee;
                    var platformFee = totalAmount * 0.01m; // 1% platform fee
                    var netAmount = netAmountAfterStripe - platformFee;

                    if (netAmount <= 0)
                    {
                        _logger.LogWarning("Net amount for PaymentIntent {PaymentId} is invalid: {NetAmount}",
                            paymentIntent.Id, netAmount);
                        throw new ValidationException("Invalid net amount", new Dictionary<string, string[]>
                        {
                            ["NetAmount"] = new[] { "Net amount must be greater than zero" }
                        });
                    }
                    #endregion Calculate Amounts

                    #region Construct PaymentRequest
                    var paymentRequest = new PaymentIntentRequest
                    {
                        UserId = userId,
                        SubscriptionId = subscriptionId,
                        Provider = "Stripe",
                        PaymentId = paymentIntent.Id,
                        TotalAmount = totalAmount,
                        PaymentProviderFee = paymentFee,
                        PlatformFee = platformFee,
                        NetAmount = netAmount,
                        Currency = charge.Currency,
                        Status = paymentIntent.Status,
                    };
                    #endregion Construct PaymentRequest

                    // Process the payment
                    var result = await ProcessPaymentIntentSucceededEvent(paymentRequest);

                    // Store the result for idempotency
                    await _idempotencyService.StoreResultAsync(idempotencyKey, result.Data);

                    return result;
                }
                catch (ValidationException ex)
                {
                    _logger.LogWarning(ex, "Validation error processing charge {ChargeId}", charge?.Id);
                    return ResultWrapper<Guid>.Failure(FailureReason.ValidationError, ex.Message);
                }
                catch (PaymentProcessingException ex)
                {
                    _logger.LogError(ex, "Payment processing error for charge {ChargeId}", charge?.Id);
                    return ResultWrapper<Guid>.Failure(FailureReason.PaymentProcessingError, ex.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process charge.updated event for Charge {ChargeId}", charge?.Id);
                    return ResultWrapper<Guid>.FromException(ex);
                }
            }
        }

        /// <summary>
        /// Processes a payment request, storing it atomically with an event and publishing a PaymentReceivedEvent.
        /// </summary>
        /// <param name="paymentRequest">The payment details to process.</param>
        /// <returns>A ResultWrapper containing the Guid of the stored event or an error.</returns>
        public async Task<ResultWrapper<Guid>> ProcessPaymentIntentSucceededEvent(PaymentIntentRequest paymentRequest)
        {
            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["PaymentId"] = paymentRequest?.PaymentId,
                ["UserId"] = paymentRequest?.UserId,
                ["SubscriptionId"] = paymentRequest?.SubscriptionId,
                ["Operation"] = "ProcessPaymentIntent",
                ["CorrelationId"] = Activity.Current?.Id
            }))
            {
                try
                {
                    #region Validate PaymentRequest
                    if (paymentRequest == null)
                    {
                        throw new ArgumentNullException(nameof(paymentRequest), "Payment request cannot be null.");
                    }

                    if (string.IsNullOrWhiteSpace(paymentRequest.UserId) || !Guid.TryParse(paymentRequest.UserId, out _))
                    {
                        throw new ValidationException("Invalid UserId", new Dictionary<string, string[]>
                        {
                            ["UserId"] = new[] { $"Invalid UserId: {paymentRequest.UserId}" }
                        });
                    }

                    if (string.IsNullOrWhiteSpace(paymentRequest.SubscriptionId) || !Guid.TryParse(paymentRequest.SubscriptionId, out Guid subscriptionId))
                    {
                        throw new ValidationException("Invalid SubscriptionId", new Dictionary<string, string[]>
                        {
                            ["SubscriptionId"] = new[] { $"Invalid SubscriptionId: {paymentRequest.SubscriptionId}" }
                        });
                    }

                    if (string.IsNullOrWhiteSpace(paymentRequest.PaymentId))
                    {
                        throw new ValidationException("Invalid PaymentId", new Dictionary<string, string[]>
                        {
                            ["PaymentId"] = new[] { $"Invalid PaymentId: {paymentRequest.PaymentId}" }
                        });
                    }

                    if (string.IsNullOrWhiteSpace(paymentRequest.Currency))
                    {
                        throw new ValidationException("Invalid Currency", new Dictionary<string, string[]>
                        {
                            ["Currency"] = new[] { $"Invalid Currency: {paymentRequest.Currency}" }
                        });
                    }

                    var assetResult = await _assetService.GetByTickerAsync(paymentRequest.Currency);
                    if (!assetResult.IsSuccess)
                    {
                        throw new ValidationException("Invalid Currency", new Dictionary<string, string[]>
                        {
                            ["Currency"] = new[] { $"Unable to fetch asset data from currency: {assetResult.ErrorMessage}" }
                        });
                    }

                    if (paymentRequest.NetAmount <= 0)
                    {
                        throw new ValidationException("Invalid Amount", new Dictionary<string, string[]>
                        {
                            ["NetAmount"] = new[] { $"Amount must be greater than zero: {paymentRequest.NetAmount}" }
                        });
                    }
                    #endregion Validate PaymentRequest

                    #region Idempotency Check
                    // Use payment ID as idempotency key
                    string idempotencyKey = $"payment-intent-{paymentRequest.PaymentId}";

                    var (resultExists, existingResult) = await _idempotencyService.GetResultAsync<Guid>(idempotencyKey);
                    if (resultExists)
                    {
                        _logger.LogInformation("Payment {PaymentId} already processed with result {Result}",
                            paymentRequest.PaymentId, existingResult);
                        return ResultWrapper<Guid>.Success(existingResult);
                    }

                    var existingPayment = await _collection.Find(p => p.PaymentProviderId == paymentRequest.PaymentId).FirstOrDefaultAsync();
                    if (existingPayment != null)
                    {
                        _logger.LogInformation("Payment {PaymentId} already stored in database", paymentRequest.PaymentId);
                        return ResultWrapper<Guid>.Success(existingPayment.Id);
                    }
                    #endregion Idempotency Check

                    var paymentData = new PaymentData
                    {
                        UserId = Guid.Parse(paymentRequest.UserId),
                        SubscriptionId = Guid.Parse(paymentRequest.SubscriptionId),
                        Provider = paymentRequest.Provider,
                        PaymentProviderId = paymentRequest.PaymentId,
                        TotalAmount = paymentRequest.TotalAmount,
                        PaymentProviderFee = paymentRequest.PaymentProviderFee,
                        PlatformFee = paymentRequest.PlatformFee,
                        NetAmount = paymentRequest.NetAmount,
                        Currency = paymentRequest.Currency,
                        Status = paymentRequest.Status,
                    };

                    Guid eventId;

                    #region Atomic Transaction
                    eventId = await ExecuteInTransactionAsync(async (session) =>
                    {
                        // Insert payment record
                        var insertPaymentResult = await InsertOneAsync(paymentData, session);
                        if (!insertPaymentResult.IsAcknowledged || insertPaymentResult.InsertedId is null)
                        {
                            throw new DatabaseException(insertPaymentResult.ErrorMessage ?? "Failed to insert payment record");
                        }

                        // Create and store the event
                        var storedEvent = new Domain.Models.Event.EventData
                        {
                            EventType = typeof(PaymentReceivedEvent).Name,
                            Payload = insertPaymentResult.InsertedId.ToString()
                        };

                        var storedEventResult = await _eventService.InsertOneAsync(storedEvent, session);
                        if (!storedEventResult.IsAcknowledged || storedEventResult.InsertedId is null)
                        {
                            throw new DatabaseException(storedEventResult.ErrorMessage ?? "Failed to store payment event");
                        }

                        return storedEventResult.InsertedId.Value;
                    });

                    // Store for idempotency outside of transaction
                    await _idempotencyService.StoreResultAsync(idempotencyKey, eventId);
                    #endregion Atomic Transaction

                    // Publish the event after successful transaction
                    await _eventService.Publish(new PaymentReceivedEvent(paymentData, eventId));

                    // Send notification to user
                    try
                    {
                        await _notificationService.CreateNotificationAsync(new NotificationData()
                        {
                            UserId = paymentRequest.UserId,
                            Message = $"A payment of {paymentRequest.NetAmount} {paymentRequest.Currency} has been received.",
                            IsRead = false
                        });
                    }
                    catch (Exception notificationEx)
                    {
                        // Log but don't fail the entire operation if notification fails
                        _logger.LogWarning(notificationEx, "Failed to send notification for payment {PaymentId}",
                            paymentRequest.PaymentId);
                    }

                    return ResultWrapper<Guid>.Success(eventId);
                }
                catch (ValidationException ex)
                {
                    _logger.LogWarning(ex, "Validation error processing payment {PaymentId}", paymentRequest?.PaymentId);
                    return ResultWrapper<Guid>.Failure(FailureReason.ValidationError, ex.Message);
                }
                catch (DatabaseException ex)
                {
                    _logger.LogError(ex, "Database error processing payment {PaymentId}", paymentRequest?.PaymentId);
                    return ResultWrapper<Guid>.Failure(FailureReason.DatabaseError, ex.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process payment request: {Message}", ex.Message);
                    return ResultWrapper<Guid>.FromException(ex);
                }
            }
        }
    }
}