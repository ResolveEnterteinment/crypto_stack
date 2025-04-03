using Application.Contracts.Requests.Payment;
using Application.Interfaces;
using Application.Interfaces.Payment;
using Domain.Constants;
using Domain.DTOs;
using Domain.DTOs.Payment;
using Domain.DTOs.Settings;
using Domain.Events;
using Domain.Exceptions;
using Domain.Models.Payment;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Polly;
using StripeLibrary;
using System.Diagnostics;

namespace Infrastructure.Services
{
    public class PaymentService : BaseService<Domain.Models.Payment.PaymentData>, IPaymentService
    {
        private readonly Dictionary<string, IPaymentProvider> _providers = new Dictionary<string, IPaymentProvider>(StringComparer.OrdinalIgnoreCase);
        private readonly IPaymentProvider _defaultProvider;

        private readonly IStripeService _stripeService;
        private readonly IAssetService _assetService;
        private readonly IEventService _eventService;
        private readonly INotificationService _notificationService;
        private readonly IIdempotencyService _idempotencyService;

        public Dictionary<string, IPaymentProvider> Providers { get => _providers; }
        public IPaymentProvider Defaultprovider => _defaultProvider;

        public PaymentService(
            IOptions<PaymentServiceSettings> paymentServiceSettings,
            IOptions<StripeSettings> stripeSettings,
            IAssetService assetService,
            IOptions<MongoDbSettings> mongoDbSettings,
            IMongoClient mongoClient,
            ILogger<PaymentService> logger,
            IMemoryCache cache,
            IEventService eventService,
            INotificationService notificationService,
            IIdempotencyService idempotencyService
            )
            : base(
                  mongoClient,
                  mongoDbSettings,
                  "payments",
                  logger,
                  cache
                  )
        {
            _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
            _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _idempotencyService = idempotencyService ?? throw new ArgumentNullException(nameof(idempotencyService));

            _stripeService = new StripeService(stripeSettings);

            Providers.Add("Stripe", (IPaymentProvider)_stripeService);

            _defaultProvider = Providers[paymentServiceSettings.Value.DefaultProvider] ?? Providers["Stripe"];
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
                        throw new PaymentApiException(
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
                        InvoiceId = paymentIntent.InvoiceId,
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
                catch (PaymentApiException ex)
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

                    var paymentData = new Domain.Models.Payment.PaymentData
                    {
                        UserId = Guid.Parse(paymentRequest.UserId),
                        SubscriptionId = Guid.Parse(paymentRequest.SubscriptionId),
                        Provider = paymentRequest.Provider,
                        PaymentProviderId = paymentRequest.PaymentId,
                        InvoiceId = paymentRequest.InvoiceId,
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

        public async Task<CheckoutSessionResponse> CreateCheckoutSessionAsync(CreateCheckoutSessionDto request)
        {
            try
            {
                // Validate request
                if (request == null)
                {
                    throw new ArgumentNullException(nameof(request), "Checkout session request cannot be null.");
                }

                if (string.IsNullOrEmpty(request.SubscriptionId))
                {
                    throw new ArgumentException("Subscription ID is required.", nameof(request.SubscriptionId));
                }

                if (string.IsNullOrEmpty(request.UserId))
                {
                    throw new ArgumentException("User ID is required.", nameof(request.UserId));
                }

                if (request.Amount <= 0)
                {
                    throw new ArgumentException("Amount must be greater than zero.", nameof(request.Amount));
                }

                // Get or use default provider
                IPaymentProvider provider = GetProvider(request.Provider);

                _logger.LogInformation("Creating checkout session for subscription {SubscriptionId} with provider {Provider}",
                    request.SubscriptionId, provider.Name);

                // Parse interval from subscription
                var interval = request.Metadata["interval"];

                // Create checkout session options
                var options = new CheckoutSessionOptions
                {
                    SuccessUrl = request.ReturnUrl ?? $"https://yourdomain.com/payment/success?session_id={{CHECKOUT_SESSION_ID}}&subscription_id={request.SubscriptionId}",
                    CancelUrl = request.CancelUrl ?? $"https://yourdomain.com/payment/cancel?subscription_id={request.SubscriptionId}",
                    Mode = request.IsRecurring ? "subscription" : "payment",
                    LineItems = new List<SessionLineItem>
                    {
                        new SessionLineItem
                        {
                            Name = request.IsRecurring ? "Crypto Investment Subscription" : "One-time Crypto Investment",
                            Description = $"{(request.IsRecurring ? interval : "one-time")} investment plan",
                            UnitAmount = (long)(request.Amount * 100), // Convert to cents
                            Currency = request.Currency.ToLowerInvariant(),
                            Quantity = 1
                        }
                    },
                    Metadata = request.Metadata ?? new Dictionary<string, string>()
                };

                // Ensure required metadata is included
                if (!options.Metadata.ContainsKey("userId"))
                {
                    options.Metadata["userId"] = request.UserId;
                }

                if (!options.Metadata.ContainsKey("subscriptionId"))
                {
                    options.Metadata["subscriptionId"] = request.SubscriptionId;
                }

                // Create the checkout session
                var sessionResult = await provider.CreateCheckoutSessionWithOptions(options);

                if (sessionResult == null || !sessionResult.IsSuccess || sessionResult.Data == null)
                {
                    throw new PaymentApiException("Failed to create checkout session", provider.Name);
                }

                var session = sessionResult.Data;

                return new CheckoutSessionResponse
                {
                    Success = !string.IsNullOrEmpty(session.Url),
                    Message = session.Status,
                    CheckoutUrl = session.Url,
                    ClientSecret = session.ClientSecret
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating checkout session: {Message}", ex.Message);
                return new CheckoutSessionResponse
                {
                    Success = false,
                    Message = $"Failed to create checkout session: {ex.Message}"
                };
            }
        }

        public async Task<PaymentStatusResponse> GetPaymentStatusAsync(string paymentId)
        {
            try
            {
                // Validate input
                if (string.IsNullOrEmpty(paymentId))
                {
                    throw new ArgumentException("Payment ID is required", nameof(paymentId));
                }

                // First check our internal payment records
                var filter = Builders<Domain.Models.Payment.PaymentData>.Filter.Eq(p => p.PaymentProviderId, paymentId);
                var payment = await _collection.Find(filter).FirstOrDefaultAsync();

                if (payment != null)
                {
                    return new PaymentStatusResponse
                    {
                        Id = payment.Id.ToString(),
                        Status = payment.Status,
                        Amount = payment.TotalAmount,
                        Currency = payment.Currency,
                        SubscriptionId = payment.SubscriptionId.ToString(),
                        CreatedAt = payment.CreatedAt,
                        UpdatedAt = payment.CreatedAt // Using CreatedAt since we don't have an UpdatedAt field
                    };
                }

                // If not found in our records, try to get from payment provider
                // We'll use the default provider (likely Stripe) since we don't know which provider
                var provider = _defaultProvider;
                if (paymentId.StartsWith("pi_"))
                {
                    // Looks like a Stripe payment intent
                    provider = _providers["Stripe"];
                }

                // For Stripe, this would check the payment intent status
                if (provider.Name == "Stripe")
                {
                    var stripeService = provider as StripeService;
                    var paymentIntent = await stripeService?.GetPaymentIntentAsync(paymentId);

                    if (paymentIntent != null)
                    {
                        return new PaymentStatusResponse
                        {
                            Id = paymentId,
                            Status = MapStripeStatusToLocal(paymentIntent.Status),
                            Amount = paymentIntent.Amount / 100m, // Convert from cents
                            Currency = paymentIntent.Currency?.ToUpperInvariant() ?? "USD",
                            SubscriptionId = paymentIntent.Metadata.TryGetValue("subscriptionId", out var subId) ? subId : "unknown",
                            CreatedAt = paymentIntent.Created,
                            UpdatedAt = DateTime.UtcNow
                        };
                    }
                }

                throw new KeyNotFoundException($"Payment with ID {paymentId} not found");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting payment status for {PaymentId}", paymentId);
                throw;
            }
        }

        private string MapStripeStatusToLocal(string stripeStatus)
        {
            return stripeStatus switch
            {
                "succeeded" => PaymentStatus.Filled,
                "processing" => PaymentStatus.Pending,
                "requires_payment_method" => PaymentStatus.Pending,
                "requires_confirmation" => PaymentStatus.Pending,
                "requires_action" => PaymentStatus.Pending,
                "requires_capture" => PaymentStatus.Pending,
                "canceled" => PaymentStatus.Failed,
                _ => PaymentStatus.Pending
            };
        }

        public async Task<PaymentDetailsDto> GetPaymentDetailsAsync(string paymentId)
        {
            try
            {
                // Validate input
                if (string.IsNullOrEmpty(paymentId))
                {
                    throw new ArgumentException("Payment ID is required", nameof(paymentId));
                }

                // Check if this is a GUID (our internal ID) or a provider ID
                if (Guid.TryParse(paymentId, out var guid))
                {
                    // It's our internal ID
                    var payment = await GetByIdAsync(guid);
                    if (payment == null)
                    {
                        throw new KeyNotFoundException($"Payment with ID {paymentId} not found");
                    }

                    return new PaymentDetailsDto
                    {
                        Id = payment.Id.ToString(),
                        UserId = payment.UserId.ToString(),
                        SubscriptionId = payment.SubscriptionId.ToString(),
                        Provider = payment.Provider,
                        PaymentProviderId = payment.PaymentProviderId,
                        TotalAmount = payment.TotalAmount,
                        PaymentProviderFee = payment.PaymentProviderFee,
                        PlatformFee = payment.PlatformFee,
                        NetAmount = payment.NetAmount,
                        Currency = payment.Currency,
                        Status = payment.Status,
                        CreatedAt = payment.CreatedAt
                    };
                }
                else
                {
                    // It's a provider ID (e.g., Stripe payment intent)
                    var filter = Builders<Domain.Models.Payment.PaymentData>.Filter.Eq(p => p.PaymentProviderId, paymentId);
                    var payment = await _collection.Find(filter).FirstOrDefaultAsync();

                    if (payment != null)
                    {
                        return new PaymentDetailsDto
                        {
                            Id = payment.Id.ToString(),
                            UserId = payment.UserId.ToString(),
                            SubscriptionId = payment.SubscriptionId.ToString(),
                            Provider = payment.Provider,
                            PaymentProviderId = payment.PaymentProviderId,
                            TotalAmount = payment.TotalAmount,
                            PaymentProviderFee = payment.PaymentProviderFee,
                            PlatformFee = payment.PlatformFee,
                            NetAmount = payment.NetAmount,
                            Currency = payment.Currency,
                            Status = payment.Status,
                            CreatedAt = payment.CreatedAt
                        };
                    }

                    // If not found in our records, try to get from payment provider
                    // We'll use the default provider (likely Stripe) since we don't know which provider
                    var provider = _defaultProvider;
                    if (paymentId.StartsWith("pi_"))
                    {
                        // Looks like a Stripe payment intent
                        provider = _providers["Stripe"];
                    }

                    // For Stripe, this would check the payment intent status
                    if (provider.Name == "Stripe")
                    {
                        var stripeService = provider as StripeService;
                        var paymentIntent = await stripeService?.GetPaymentIntentAsync(paymentId);

                        if (paymentIntent != null)
                        {
                            string userId = paymentIntent.Metadata.TryGetValue("userId", out var uid) ? uid : "unknown";
                            string subscriptionId = paymentIntent.Metadata.TryGetValue("subscriptionId", out var subId) ? subId : "unknown";

                            return new PaymentDetailsDto
                            {
                                Id = "external",
                                UserId = userId,
                                SubscriptionId = subscriptionId,
                                Provider = "Stripe",
                                PaymentProviderId = paymentId,
                                TotalAmount = paymentIntent.Amount / 100m, // Convert from cents
                                PaymentProviderFee = 0, // We don't know the fee yet
                                PlatformFee = paymentIntent.Amount / 100m * 0.01m, // 1% platform fee
                                NetAmount = paymentIntent.Amount / 100m * 0.99m, // Net after platform fee
                                Currency = paymentIntent.Currency?.ToUpperInvariant() ?? "USD",
                                Status = MapStripeStatusToLocal(paymentIntent.Status),
                                CreatedAt = paymentIntent.Created
                            };
                        }
                    }

                    throw new KeyNotFoundException($"Payment with ID {paymentId} not found");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting payment details for {PaymentId}", paymentId);
                throw;
            }
        }

        public async Task<ResultWrapper> CancelPaymentAsync(string paymentId)
        {
            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["PaymentId"] = paymentId,
                ["Operation"] = "CancelPayment",
                ["CorrelationId"] = Activity.Current?.Id
            }))
            {
                try
                {
                    // Define idempotency key from the charge ID
                    string idempotencyKey = $"payment-cancelled-{paymentId}";

                    // Check for existing processing
                    var (resultExists, existingResult) = await _idempotencyService.GetResultAsync<Guid>(idempotencyKey);
                    if (resultExists)
                    {
                        _logger.LogInformation("Payment {PaymentId} already processed with result {Result}", paymentId, existingResult);
                        return ResultWrapper.Success($"Payment {paymentId} already processed with result {existingResult}");
                    }
                    // Validate input
                    if (string.IsNullOrEmpty(paymentId))
                    {
                        throw new ArgumentException("Payment ID is required", nameof(paymentId));
                    }

                    // Determine if this is our internal ID or provider ID
                    bool isInternalId = Guid.TryParse(paymentId, out var guid);
                    PaymentData payment = null;

                    if (isInternalId)
                    {
                        // It's our internal ID
                        payment = await GetByIdAsync(guid);
                        paymentId = payment.PaymentProviderId; // Use provider ID for cancellation
                    }
                    else
                    {
                        // It's a provider ID, check if we have it in our system
                        var filter = Builders<PaymentData>.Filter.Eq(p => p.PaymentProviderId, paymentId);
                        payment = await GetOneAsync(filter);
                    }

                    if (payment == null)
                    {
                        throw new KeyNotFoundException($"Payment with ID {paymentId} not found");
                    }

                    // If we have a payment record and it's not in a cancellable state, return error
                    if (payment != null && !IsCancellable(payment.Status))
                    {
                        throw new InvalidOperationException($"Payment cannot be cancelled. Current status: {payment.Status}");
                    }

                    IPaymentProvider provider = Providers[payment.Provider] ?? _defaultProvider;

                    var cancelResult = await provider.CancelPaymentAsync(paymentId);
                    // For Stripe provider, cancel the payment intent
                    if (!cancelResult.IsSuccess)
                    {
                        _logger.LogError("Stripe error cancelling payment {PaymentId}: {Message}", paymentId, cancelResult.ErrorMessage);
                        throw new PaymentApiException($"Failed to cancel payment: {cancelResult.ErrorMessage}", provider.Name, paymentId);
                    }

                    Guid eventId;

                    #region Atomic Transaction
                    eventId = await ExecuteInTransactionAsync(async (session) =>
                    {
                        var updatedFields = new Dictionary<string, object>
                        {
                            ["Status"] = PaymentStatus.Failed
                        };

                        var updatePaymentResult = await UpdateOneAsync(payment.Id, updatedFields, session);

                        if (!updatePaymentResult.IsAcknowledged || updatePaymentResult.ModifiedCount == 0)
                        {
                            throw new DatabaseException("Failed to update payment record");
                        }

                        // Create and store the event
                        var storedEvent = new Domain.Models.Event.EventData
                        {
                            EventType = typeof(PaymentCancelledEvent).Name,
                            Payload = updatePaymentResult.UpsertedId.ToString()
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
                    await _eventService.Publish(new PaymentCancelledEvent(payment, eventId));

                    return ResultWrapper.Success("Payment is cancelled successfully.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error cancelling payment {PaymentId}", paymentId);
                    return ResultWrapper.FromException(ex);
                }
            }
        }

        private bool IsCancellable(string status)
        {
            // Only payments in certain states can be cancelled
            return status == PaymentStatus.Pending ||
                   status == PaymentStatus.Queued;
        }
        private IPaymentProvider GetProvider(string provider)
        {
            if (string.IsNullOrEmpty(provider)) return _defaultProvider;
            return Providers.ContainsKey(provider) ? Providers[provider] : _defaultProvider;
        }
    }
}