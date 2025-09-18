using Application.Contracts.Requests.Payment;
using Application.Contracts.Responses.Payment;
using Application.Interfaces;
using Application.Interfaces.Asset;
using Application.Interfaces.Payment;
using Domain.Constants.Logging;
using Domain.Constants.Payment;
using Domain.DTOs;
using Domain.DTOs.Logging;
using Domain.DTOs.Payment;
using Domain.DTOs.Settings;
using Domain.DTOs.User;
using Domain.Events;
using Domain.Events.Payment;
using Domain.Events.Subscription;
using Domain.Exceptions;
using Domain.Models.Email;
using Domain.Models.Payment;
using Infrastructure.Services.Base;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using StripeLibrary;

namespace Infrastructure.Services
{
    public class PaymentService : BaseService<PaymentData>, IPaymentService
    {
        private readonly Dictionary<string, IPaymentProvider> _providers = new(StringComparer.OrdinalIgnoreCase);
        private readonly IPaymentProvider _defaultProvider;
        private readonly StripeService _stripeService;
        private readonly IAssetService _assetService;
        private readonly IUserService _userService;
        private readonly IEmailService _emailService;
        private readonly IIdempotencyService _idempotencyService;

        public IReadOnlyDictionary<string, IPaymentProvider> Providers => _providers;
        public IPaymentProvider DefaultProvider => _defaultProvider;

        public PaymentService(
            IServiceProvider serviceProvider,
            IOptions<PaymentServiceSettings> paymentSettings,
            IOptions<StripeSettings> stripeSettings,
            IAssetService assetService,
            IUserService userService,
            IEmailService emailService,
            IIdempotencyService idempotencyService
        ) : base(serviceProvider)
        {
            _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));
            _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
            _idempotencyService = idempotencyService ?? throw new ArgumentNullException(nameof(idempotencyService));
            _stripeService = new StripeService(stripeSettings);
            _providers.Add("Stripe", _stripeService as IPaymentProvider);
            _defaultProvider = _providers[
                paymentSettings.Value.DefaultProvider
                    ?? throw new InvalidOperationException("DefaultProvider not configured")];
        }

        public async Task<CrudResult<PaymentData>> UpdateStatusAsync(Guid id, string status)
        {
            return await _repository.UpdateAsync(id,
                new UpdateDefinitionBuilder<PaymentData>()
                .Set(p => p.Status, status));
        }
        public async Task<ResultWrapper<PaymentData>> ProcessInvoicePaidEvent(InvoiceRequest invoice)
        {
            var key = $"invoice-paid-{invoice.Id}";

            var (hit, _) = await _idempotencyService.GetResultAsync<Guid>(key);
            if (hit)
            {
                _loggingService.LogWarning($"Invoice {invoice.Id} already processed.");
                return ResultWrapper<PaymentData>.Success();
            }

            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Payment",
                    FileName = "PaymentService",
                    OperationName = "ProcessInvoicePaidEvent(InvoiceRequest invoice)",
                    State = {
                        ["InvoiceId"] = invoice.Id,
                        ["UserId"] = invoice.UserId,
                        ["SubscriptionId"] = invoice.SubscriptionId,
                        ["Amount"] = invoice.Amount,
                        ["Provider"] = invoice.Provider,
                        ["Currency"] = invoice.Currency
                    },
                    LogLevel = LogLevel.Critical
                },
                async () =>
                {
                    ValidateInvoiceRequest(invoice);

                    // Check existing
                    var existingWr = await GetOneAsync(
                        Builders<PaymentData>.Filter.Eq(p => p.InvoiceId, invoice.Id));

                    if (existingWr != null && existingWr.Data != null)
                    {
                        _loggingService.LogWarning($"Invoice data with ID {invoice.Id} already present.");
                        return existingWr.Data;
                    }

                    // Calculate amounts
                    var (total, fee, platformFee, net) = await CalculatePaymentFees(invoice);

                    var paymentData = new PaymentData
                    {
                        UserId = Guid.Parse(invoice.UserId),
                        SubscriptionId = Guid.Parse(invoice.SubscriptionId),
                        ProviderSubscriptionId = invoice.ProviderSubscripitonId,
                        Provider = invoice.Provider,
                        PaymentProviderId = invoice.PaymentIntentId,
                        InvoiceId = invoice.Id,
                        TotalAmount = total,
                        PaymentProviderFee = fee,
                        PlatformFee = platformFee,
                        NetAmount = net,
                        Currency = invoice.Currency,
                        Status = invoice.Status
                    };

                    var insertWr = await InsertAsync(paymentData);

                    if (insertWr == null || !insertWr.IsSuccess)
                        throw new DatabaseException(insertWr?.ErrorMessage ?? "Insert result returned null");

                    await _idempotencyService.StoreResultAsync(key, paymentData.Id);

                    return paymentData;
                })
                .WithCriticalOperationResilience()
                .WithPerformanceMonitoring(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15))
                .WithContext("OperationType", "InvoiceProcessing")
                .OnSuccess(async (paymentData) =>
                {
                    if (paymentData != null)
                    {
                        await _eventService!.PublishAsync(new PaymentReceivedEvent(paymentData, _loggingService.Context));
                        await SendPaymentReceivedNotification(invoice);

                        _loggingService.LogInformation("Successfully processed invoice {InvoiceId} for payment {PaymentId}",
                            invoice.Id, paymentData.Id);
                    }
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper> ProcessCheckoutSessionCompletedAsync(SessionDto session)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Payment",
                    FileName = "PaymentService",
                    OperationName = "ProcessCheckoutSessionCompletedAsync(SessionDto session)",
                    State = {
                        ["SessionId"] = session?.Id,
                        ["SessionProvider"] = session?.Provider,
                        ["SessionStatus"] = session?.Status,
                        ["SubscriptionId"] = session?.SubscriptionId,
                        ["InvoiceId"] = session?.InvoiceId
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    await _eventService!.PublishAsync(new CheckoutSessionCompletedEvent(session, _loggingService.Context));
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<SessionDto>> CreateCheckoutSessionAsync(CreateCheckoutSessionDto request)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Payment",
                    FileName = "PaymentService",
                    OperationName = "CreateCheckoutSessionAsync(CreateCheckoutSessionDto request)",
                    State = {
                        ["UserId"] = request.UserId,
                        ["SubscriptionId"] = request.SubscriptionId
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    ValidateCheckoutRequest(request);

                    var provider = GetProvider(request.Provider);
                    var customerId = await GetOrCreateStripeCustomerAsync(
                        request.UserId, request.UserEmail);

                    var options = BuildCheckoutSessionOptions(request, customerId);
                    var sessionWr = await provider.CreateCheckoutSessionWithOptions(options);
                    if (!sessionWr.IsSuccess || sessionWr.Data == null)
                        throw new PaymentApiException("Failed to create session", provider.Name);

                    await _eventService!.PublishAsync(new CheckoutSessionCreatedEvent(sessionWr.Data, _loggingService.Context));
                    return sessionWr.Data;
                })
                .WithHttpResilience() // Add proper resilience pattern
                .WithPerformanceMonitoring(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(10))
                .WithContext("OperationType", "CheckoutSessionCreation")
                .WithContext("PaymentProvider", request.Provider ?? "Default")
                .OnError(async (ex) =>
                {
                    // Log specific payment provider errors
                    if (ex is PaymentApiException paymentEx)
                    {
                        await _loggingService.LogTraceAsync(
                            $"Payment provider {paymentEx.PaymentProvider} failed to create checkout session: {paymentEx.Message}",
                            level: LogLevel.Error,
                            requiresResolution: true);
                    }
                })
                .OnCriticalError(async (ex) =>
                {
                    // Handle critical failures that might affect multiple users
                    await _loggingService.LogTraceAsync(
                        $"Critical failure in checkout session creation: {ex.Message}",
                        level: LogLevel.Critical,
                        requiresResolution: true);

                    // Could trigger alerts or fallback mechanisms here
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<PaymentDetailsDto>> GetPaymentDetailsAsync(string paymentId)
        {
            if (string.IsNullOrWhiteSpace(paymentId))
                throw new ArgumentException("Payment ID is required", nameof(paymentId));

            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Payment",
                    FileName = "PaymentService",
                    OperationName = "GetPaymentDetailsAsync(string paymentId)",
                    State = {
                ["PaymentId"] = paymentId
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    // Step 1: Try to get from local database by GUID first
                    if (Guid.TryParse(paymentId, out var guid) && guid != Guid.Empty)
                    {
                        var wr = await GetByIdAsync(guid);
                        if (wr != null && wr.IsSuccess && wr.Data != null)
                        {
                            return new PaymentDetailsDto(wr.Data);
                        }
                    }

                    // Step 2: Try to get from local database by PaymentProviderId
                    var dbWr = await GetOneAsync(
                        Builders<PaymentData>.Filter.Eq(p => p.PaymentProviderId, paymentId));

                    if (dbWr != null && dbWr.IsSuccess && dbWr.Data != null)
                    {
                        return new PaymentDetailsDto(dbWr.Data);
                    }

                    // Step 3: Fallback to external payment provider (Stripe)
                    var provider = GetProviderForPaymentId(paymentId);
                    if (provider.Name == "Stripe")
                    {
                        var dto = await GetStripePaymentDetailsAsync(paymentId);
                        if (dto != null)
                        {
                            return dto;
                        }
                    }

                    // Step 4: Not found anywhere
                    throw new KeyNotFoundException($"Payment details not found for {paymentId}");
                })
                .WithComprehensiveResilience(
                    maxRetries: 2,                    // Limited retries for read operations
                    timeout: TimeSpan.FromSeconds(10), // Reasonable timeout for DB + external API
                    failureRatio: 0.3                 // Circuit breaker threshold
                )
                .WithPerformanceMonitoring(
                    TimeSpan.FromSeconds(2),   // Warning threshold - DB queries should be fast
                    TimeSpan.FromSeconds(8)    // Error threshold - before overall timeout
                )
                .WithContext("OperationType", "PaymentDetailsLookup")
                .WithContext("PaymentProvider", GetProviderForPaymentId(paymentId).Name)
                .OnError(async (ex) =>
                {
                    // Handle specific error scenarios
                    if (ex is KeyNotFoundException)
                    {
                        await _loggingService.LogTraceAsync(
                            $"Payment {paymentId} not found in database or payment provider",
                            level: LogLevel.Warning);
                    }
                    else if (ex is HttpRequestException || ex is TaskCanceledException)
                    {
                        await _loggingService.LogTraceAsync(
                            $"External payment provider API failed for payment {paymentId}: {ex.Message}",
                            level: LogLevel.Error,
                            requiresResolution: true);
                    }
                    else if (ex is DatabaseException)
                    {
                        await _loggingService.LogTraceAsync(
                            $"Database error retrieving payment details for {paymentId}: {ex.Message}",
                            level: LogLevel.Error,
                            requiresResolution: true);
                    }
                })
                .OnCriticalError(async (ex) =>
                {
                    // Handle critical failures that might indicate system-wide issues
                    await _loggingService.LogTraceAsync(
                        $"Critical failure getting payment details for {paymentId}: {ex.Message}",
                        level: LogLevel.Critical,
                        requiresResolution: true);
                })
                .ExecuteAsync();
        }
        public async Task<ResultWrapper<PaymentData?>> GetByProviderIdAsync(string paymentProviderId)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Payment",
                    FileName = "PaymentService",
                    OperationName = "GetByProviderIdAsync(string paymentProviderId)",
                    State = {
                        ["PaymentProviderId"] = paymentProviderId,
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    var paymentResult = await GetOneAsync(new FilterDefinitionBuilder<PaymentData>().Eq(t => t.PaymentProviderId, paymentProviderId));
                    if (paymentResult == null || !paymentResult.IsSuccess)
                        throw new KeyNotFoundException("Failed to fetch payment transactions");
                    return paymentResult.Data;
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<CrudResult<PaymentData>>> CancelPaymentAsync(string paymentId)
        {
            // Step 1: Check idempotency first to avoid duplicate cancellations
            var key = $"payment-cancelled-{paymentId}";
            var (hit, _) = await _idempotencyService.GetResultAsync<Guid>(key);
            if (hit)
            {
                _loggingService.LogInformation("Payment {PaymentId} already cancelled (idempotency check)", paymentId);
                return ResultWrapper<CrudResult<PaymentData>>.SuccessEmpty;
            }

            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Payment",
                    FileName = "PaymentService",
                    OperationName = "CancelPaymentAsync(string paymentId)",
                    State = {
                        ["PaymentId"] = paymentId,
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    // Step 2: Fetch and validate payment data
                    var paymentData = await GetPaymentDataForCancel(paymentId)
                                      ?? throw new KeyNotFoundException($"Payment {paymentId} not found");

                    // Step 3: Validate payment state
                    if (!IsCancellable(paymentData.Status))
                        throw new InvalidOperationException($"Cannot cancel payment in status {paymentData.Status}. Only {PaymentStatus.Pending} and {PaymentStatus.Queued} payments can be cancelled.");

                    // Step 4: Determine provider and cancel with external provider
                    var provider = GetProviderForPaymentId(paymentId);
                    var cancelWr = await provider.CancelPaymentAsync(paymentId, "requested_by_customer");

                    if (!cancelWr.IsSuccess)
                        throw new PaymentApiException(
                            cancelWr.ErrorMessage ?? "Payment cancellation failed at provider level",
                            provider.Name,
                            paymentId);

                    // Step 5: Update local database status
                    var updateWr = await UpdateAsync(paymentData.Id, new
                    {
                        Status = PaymentStatus.Failed,
                        UpdatedAt = DateTime.UtcNow,
                        CancellationReason = "requested_by_customer"
                    });

                    if (updateWr == null || !updateWr.IsSuccess)
                        throw new DatabaseException($"Failed to update payment status: {updateWr?.ErrorMessage ?? "Update result returned null"}");

                    // Step 6: Publish cancellation event for downstream processing
                    await _eventService!.PublishAsync(new PaymentCancelledEvent(paymentData, _loggingService.Context));

                    // Step 7: Store idempotency key to prevent duplicate processing
                    await _idempotencyService.StoreResultAsync(key, paymentData.Id);

                    _loggingService.LogInformation("Successfully cancelled payment {PaymentId} with provider {Provider}",
                        paymentId, provider.Name);
                    return updateWr.Data;
                })
                .WithCriticalOperationResilience() // Use critical operation pattern for payment cancellations
                .WithPerformanceMonitoring(
                    TimeSpan.FromSeconds(3),   // Warning threshold - cancellations should be relatively fast
                    TimeSpan.FromSeconds(10)   // Error threshold - before timeout
                )
                .WithContext("OperationType", "PaymentCancellation")
                .WithContext("PaymentProvider", GetProviderForPaymentId(paymentId).Name)
                .OnSuccess(async (CrudResult<PaymentData> cancelResult) =>
                {
                    var paymentData = cancelResult.Documents.First();
                    await _notificationService.CreateAndSendNotificationAsync(new NotificationData
                    {
                        UserId = paymentData.UserId.ToString(),
                        Message = $"Payment cancellation confirmed for {paymentData?.TotalAmount:C} {paymentData?.Currency}",
                        IsRead = false
                    });
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<CrudResult<PaymentData>>> UpdatePaymentRetryInfoAsync(
            Guid paymentId,
            int attemptCount,
            DateTime lastAttemptAt,
            DateTime nextRetryAt,
            string failureReason)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Payment",
                    FileName = "PaymentService",
                    OperationName = "UpdatePaymentRetryInfoAsync(Guid paymentId, int attemptCount, DateTime lastAttemptAt, DateTime nextRetryAt, string failureReason)",
                    State = {
                        ["PaymentId"] = paymentId,
                        ["AttemptCount"] = attemptCount,
                        ["NextRetryAt"] = nextRetryAt
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    var updateFields = new Dictionary<string, object>
                    {
                        ["AttemptCount"] = attemptCount,
                        ["LastAttemptAt"] = lastAttemptAt,
                        ["NextRetryAt"] = nextRetryAt,
                        ["FailureReason"] = failureReason,
                        ["Status"] = PaymentStatus.Failed
                    };

                    var updateResult = await UpdateAsync(paymentId, updateFields);
                    if (updateResult == null || !updateResult.IsSuccess)
                        throw new DatabaseException(updateResult?.ErrorMessage ?? "Failed to update Payment");

                    return updateResult.Data;
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<List<PaymentData>>> GetPendingRetriesAsync()
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Payment",
                    FileName = "PaymentService",
                    OperationName = "GetPendingRetriesAsync()",
                    State = [],
                },
                async () =>
                {
                    var now = DateTime.UtcNow;

                    // Find payments with: status=failed, nextRetryAt <= now, attemptCount < max attempts
                    var filter = Builders<PaymentData>.Filter.And(
                        Builders<PaymentData>.Filter.Eq(p => p.Status, PaymentStatus.Failed),
                        Builders<PaymentData>.Filter.Lte(p => p.NextRetryAt, now),
                        Builders<PaymentData>.Filter.Lt(p => p.AttemptCount, 3) // Max attempts
                    );

                    var pendingRetries = await GetManyAsync(filter);
                    return pendingRetries.Data;
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper> RetryPaymentAsync(Guid paymentId)
        {
            // Step 1: Idempotency check to prevent duplicate retries
            var idempotencyKey = $"payment-retry-{paymentId}-{DateTime.UtcNow:yyyyMMddHH}";
            var (hit, _) = await _idempotencyService.GetResultAsync<Guid>(idempotencyKey);
            if (hit)
            {
                _loggingService.LogInformation("Payment {PaymentId} retry already initiated this hour", paymentId);
                return ResultWrapper.Success();
            }

            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Payment",
                    FileName = "PaymentService",
                    OperationName = "RetryPaymentAsync(Guid paymentId)",
                    State = {
                        ["PaymentId"] = paymentId
                    },
                    LogLevel = LogLevel.Critical
                },
                async () =>
                {
                    // Step 2: Get and validate payment data
                    var paymentResult = await GetByIdAsync(paymentId);
                    if (!paymentResult.IsSuccess || paymentResult.Data == null)
                        throw new KeyNotFoundException($"Payment {paymentId} not found");

                    var payment = paymentResult.Data;

                    // Step 3: Validate payment state and retry eligibility
                    if (payment.Status != PaymentStatus.Failed)
                        throw new PaymentApiException(
                            $"Cannot retry payment in status {payment.Status}. Only failed payments can be retried.", 
                            payment.Provider, 
                            payment.Id.ToString());

                    if (payment.AttemptCount >= 3) // Max retry attempts
                        throw new PaymentApiException($"Payment has exceeded maximum retry attempts ({payment.AttemptCount}/3)",
                            payment.Provider,
                            payment.Id.ToString());

                    if (payment.NextRetryAt > DateTime.UtcNow)
                        throw new PaymentApiException($"Payment is not eligible for retry until {payment.NextRetryAt:yyyy-MM-dd HH:mm:ss} UTC",
                            payment.Provider,
                            payment.Id.ToString());

                    // Step 4: Validate required data for retry
                    if (string.IsNullOrEmpty(payment.ProviderSubscriptionId))
                        throw new ValidationException("Invalid argument", 
                            new Dictionary<string, string[]>
                            {
                                ["ProviderSubscriptionId"] = ["Provider subscription ID required"]
                            });

                    // Step 5: Get payment provider
                    var provider = GetProvider(payment.Provider);

                    // Step 6: Attempt to retry the payment with the payment provider
                    var retryResult = await _stripeService.RetryPaymentAsync(
                        payment.PaymentProviderId,
                        payment.ProviderSubscriptionId);

                    if (!retryResult.IsSuccess)
                        throw new PaymentApiException(
                            retryResult.ErrorMessage ?? "Payment retry failed at provider level",
                            provider.Name,
                            payment.PaymentProviderId);

                    // Step 7: Update payment status to pending for webhook processing
                    var updateFields = new Dictionary<string, object>
                    {
                        ["Status"] = PaymentStatus.Pending,
                        ["AttemptCount"] = payment.AttemptCount + 1,
                        ["LastAttemptAt"] = DateTime.UtcNow,
                        ["UpdatedAt"] = DateTime.UtcNow
                    };

                    var updateResult = await UpdateAsync(paymentId, updateFields);
                    if (!updateResult.IsSuccess)
                        throw new DatabaseException($"Failed to update payment retry status: {updateResult.ErrorMessage}");

                    // Step 8: Store idempotency key
                    await _idempotencyService.StoreResultAsync(idempotencyKey, paymentId);

                    _loggingService.LogInformation(
                        "Successfully initiated retry for payment {PaymentId}, attempt {AttemptCount}",
                        paymentId, payment.AttemptCount + 1);
                })
                .WithCriticalOperationResilience() // Use critical operation pattern for payment retries
                .WithPerformanceMonitoring(
                    TimeSpan.FromSeconds(5),   // Warning threshold - retries should be reasonably fast
                    TimeSpan.FromSeconds(15)   // Error threshold - before timeout
                )
                .WithContext("OperationType", "PaymentRetry")
                .ExecuteAsync();
        }

        public async Task<ResultWrapper> ProcessPaymentFailedAsync(PaymentIntentRequest paymentIntentRequest)
        {
            if (paymentIntentRequest == null)
                throw new ArgumentNullException(nameof(paymentIntentRequest));

            // Step 1: Idempotency check to prevent duplicate processing
            var idempotencyKey = $"payment-failed-{paymentIntentRequest.PaymentId}-{paymentIntentRequest.InvoiceId}";
            var (hit, _) = await _idempotencyService.GetResultAsync<Guid>(idempotencyKey);
            if (hit)
            {
                _loggingService.LogWarning("Payment failure event for {PaymentId} already processed (idempotency check)", paymentIntentRequest.PaymentId);
                return ResultWrapper.Success();
            }

            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Payment",
                    FileName = "PaymentService",
                    OperationName = "ProcessPaymentFailedAsync(PaymentIntentRequest paymentIntentRequest)",
                    State = {
                        ["PaymentProviderId"] = paymentIntentRequest.PaymentId,
                        ["UserId"] = paymentIntentRequest.UserId,
                        ["SubscriptionId"] = paymentIntentRequest.SubscriptionId,
                        ["InvoiceId"] = paymentIntentRequest.InvoiceId,
                        ["Amount"] = paymentIntentRequest.Amount,
                        ["Currency"] = paymentIntentRequest.Currency,
                        ["Status"] = paymentIntentRequest.Status
                    },
                    LogLevel = LogLevel.Critical // Payment failures are critical business events
                },
                async () =>
                {
                    

                    _loggingService.LogInformation("Processing payment failure for {PaymentId}, Invoice: {InvoiceId}, Amount: {Amount}",
                        paymentIntentRequest.PaymentId, paymentIntentRequest.InvoiceId, paymentIntentRequest.Amount / 100m);

                    // Step 2: Try to find existing payment record
                    var existingPayment = await GetByProviderIdAsync(paymentIntentRequest.InvoiceId);
                    PaymentData paymentData;

                    if (existingPayment == null || !existingPayment.IsSuccess || existingPayment.Data == null)
                    {
                        // Step 3: Create new payment record from Stripe invoice data
                        paymentData = await CreateNewFailedPaymentRecord(paymentIntentRequest);
                    }
                    else
                    {
                        // Step 4: Update existing payment record
                        paymentData = await UpdateExistingPaymentRecord(existingPayment.Data, paymentIntentRequest);
                    }

                    // Step 5: Publish payment failure event for downstream processing
                    await _eventService!.PublishAsync(new SubscriptionPaymentFailedEvent(
                        paymentData,
                        paymentData.FailureReason ?? "Payment failed",
                        paymentData.AttemptCount,
                        _loggingService.Context
                    ));

                    // Step 6: Send user notification
                    await SendPaymentFailedNotification(paymentIntentRequest);

                    // Step 7: Store idempotency key
                    await _idempotencyService.StoreResultAsync(idempotencyKey, paymentData.Id);

                    _loggingService.LogInformation("Successfully processed payment failure for {PaymentId}", paymentIntentRequest.PaymentId);
                })
                .WithCriticalOperationResilience() // Use critical operation pattern for payment failures
                .WithPerformanceMonitoring(
                    TimeSpan.FromSeconds(5),   // Warning threshold - payment failure processing should be reasonably fast
                    TimeSpan.FromSeconds(15)   // Error threshold - before timeout
                )
                .WithContext("OperationType", "PaymentFailureProcessing")
                .WithContext("PaymentProvider", "Stripe")
                .WithContext("FailureReason", paymentIntentRequest.LastPaymentError ?? "Unknown")
                .OnCriticalError(async (ex) =>
                {
                    // Handle critical failures that might indicate systemic issues
                    await _loggingService.LogTraceAsync(
                        $"Critical failure processing payment failure for {paymentIntentRequest.PaymentId}: {ex.Message}. This may indicate a systemic payment processing issue.",
                        level: LogLevel.Critical,
                        requiresResolution: true);
                })
                .OnSuccess(async () =>
                {
                    // Optional: Additional success processing
                    _loggingService.LogInformation("Payment failure processing completed successfully for {PaymentId}", paymentIntentRequest.PaymentId);
                })
                .ExecuteAsync();
        }
        public async Task<ResultWrapper> ProcessSetupIntentSucceededAsync(Guid subscriptionId)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Payment",
                    FileName = "PaymentService",
                    OperationName = "ProcessCheckoutSessionCompletedAsync(SessionDto session)",
                    State = {
                        ["SubscriptionId"] = subscriptionId,
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    // Instead of directly checking subscription status, publish an event
                    await _eventService.PublishAsync(new PaymentMethodUpdatedEvent(subscriptionId, "", _loggingService.Context));

                    // The rest is handled by event handlers in SubscriptionService
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<SessionDto>> CreateUpdatePaymentMethodSessionAsync(string userId, string subscriptionId)
        {
            // Step 1: Input validation first (before resilience wrapper)
            if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out _))
                throw new ArgumentException("Invalid userId", nameof(userId));

            if (string.IsNullOrEmpty(subscriptionId) || !Guid.TryParse(subscriptionId, out var parsedSubscriptionId))
                throw new ArgumentException("Invalid subscriptionId", nameof(subscriptionId));

            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Payment",
                    FileName = "PaymentService",
                    OperationName = "CreateUpdatePaymentMethodSessionAsync(string userId, string subscriptionId)",
                    State = {
                        ["UserId"] = userId,
                        ["SubscriptionId"] = subscriptionId,
                    },
                    LogLevel = LogLevel.Critical // Payment method updates are important but not critical
                },
                async () =>
                {
                    // Get the payment data for this subscription to find the provider subscription ID
                    var paymentResult = await GetLatestPaymentAsync(parsedSubscriptionId);

                    if (!paymentResult.IsSuccess || paymentResult.Data == null)
                        throw new KeyNotFoundException($"No payment found for subscription {subscriptionId}");

                    var payment = paymentResult.Data;

                    if (string.IsNullOrEmpty(payment.ProviderSubscriptionId))
                        throw new InvalidOperationException("No provider subscription ID found");

                    // Create checkout session for updating payment method
                    var sessionResult = await _stripeService.CreateUpdatePaymentMethodSessionAsync(
                        payment.ProviderSubscriptionId,
                        new Dictionary<string, string>
                        {
                            ["userId"] = userId,
                            ["subscriptionId"] = subscriptionId
                        });

                    if (sessionResult == null || !sessionResult.IsSuccess || string.IsNullOrEmpty(sessionResult.Data?.Url))
                    {
                        await _loggingService.LogTraceAsync($"Failed to create update payment method session: {sessionResult.ErrorMessage ?? "Session result returned null"}",
                            level: LogLevel.Error,
                            requiresResolution: true);
                        throw new PaymentApiException("Failed to create update payment method session", "Stripe");
                    }

                    return sessionResult.Data;
                })
                .WithHttpResilience() // HTTP resilience for external Stripe API calls
                .WithPerformanceMonitoring(
                    TimeSpan.FromSeconds(3),   // Warning threshold - session creation should be fast
                    TimeSpan.FromSeconds(10)   // Error threshold - reasonable timeout for external API
                )
                .WithContext("OperationType", "PaymentMethodSessionCreation")
                .WithContext("PaymentProvider", "Stripe")
                .WithContext("SubscriptionId", subscriptionId)
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<List<PaymentData>>> GetPaymentsForSubscriptionAsync(Guid subscriptionId)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Payment",
                    FileName = "PaymentService",
                    OperationName = "GetPaymentsForSubscriptionAsync(Guid subscriptionId)",
                    State = {
                ["SubscriptionId"] = subscriptionId
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    var filter = Builders<PaymentData>.Filter.Eq(p => p.SubscriptionId, subscriptionId);
                    var sort = Builders<PaymentData>.Sort.Descending(p => p.CreatedAt);

                    // Get payments using the resilient database operation
                    var payments = await GetManyAsync(filter);
                    if (!payments.IsSuccess)
                        throw new DatabaseException(payments.ErrorMessage);

                    // Sort the payments by date (most recent first)
                    var sortedPayments = payments.Data.OrderByDescending(p => p.CreatedAt);

                    return sortedPayments.ToList();
                })
                .WithMongoDbReadResilience() // Use read-specific resilience for database queries
                .WithPerformanceMonitoring(
                    TimeSpan.FromSeconds(2),   // Warning threshold - DB queries should be fast
                    TimeSpan.FromSeconds(5)    // Error threshold - reasonable for read operations
                )
                .WithContext("OperationType", "PaymentDataRetrieval")
                .WithContext("SubscriptionId", subscriptionId.ToString())
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<PaymentData?>> GetLatestPaymentAsync(Guid subscriptionId)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Payment",
                    FileName = "PaymentService",
                    OperationName = "GetLatestPaymentAsync(Guid subscriptionId)",
                    State = {
                        ["SubscriptionId"] = subscriptionId
                    },
                },
                async () =>
                {
                    var filter = Builders<PaymentData>.Filter.Eq(p => p.SubscriptionId, subscriptionId);
                    var sort = Builders<PaymentData>.Sort.Descending(p => p.CreatedAt);

                    // Use the repository to get the most recent payment
                    var latestPayment = await _repository.GetOneAsync(filter, sort);

                    return latestPayment;
                })
                .WithMongoDbReadResilience()
                .WithPerformanceMonitoring(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3))
                .ExecuteAsync();
        }
        public async Task<ResultWrapper<FetchUpdatePaymentResponse>> FetchPaymentsBySubscriptionAsync(string stripeSubscriptionId)
        {
            // Step 1: Input validation first (before resilience wrapper)
            if (string.IsNullOrEmpty(stripeSubscriptionId))
                throw new ArgumentNullException(nameof(stripeSubscriptionId), "Stripe subscription ID cannot be null or empty");

            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Payment",
                    FileName = "PaymentService",
                    OperationName = "FetchPaymentsBySubscriptionAsync(string stripeSubscriptionId)",
                    State = {
                        ["StripeSubscriptionId"] = stripeSubscriptionId
                    },
                    LogLevel = LogLevel.Critical // Payment synchronization is critical for data consistency
                },
                async () =>
                {
                    _loggingService.LogInformation("Starting payment fetch for Stripe subscription {StripeSubscriptionId}", stripeSubscriptionId);

                    // Step 1: Get all invoices from Stripe for this subscription
                    var stripeInvoices = await _stripeService.GetSubscriptionInvoicesAsync(stripeSubscriptionId);

                    if (stripeInvoices == null || !stripeInvoices.Any())
                    {
                        _loggingService.LogInformation("No paid invoices found in Stripe for subscription {StripeSubscriptionId}", stripeSubscriptionId);
                        return new FetchUpdatePaymentResponse
                        {
                            TotalCount = 0,
                            ProcessedCount = 0,
                        };
                    }

                    // Step 2: Get existing payment records from our database for this subscription
                    var existingPaymentsFilter = Builders<PaymentData>.Filter.Eq(p => p.ProviderSubscriptionId, stripeSubscriptionId);
                    var existingPaymentsResult = await GetManyAsync(existingPaymentsFilter);

                    if (!existingPaymentsResult.IsSuccess)
                    {
                        throw new DatabaseException(existingPaymentsResult.ErrorMessage ?? "Failed to fetch existing payments");
                    }

                    var existingPayments = existingPaymentsResult.Data?.ToList() ?? new List<PaymentData>();
                    var existingInvoiceIds = existingPayments.Select(p => p.InvoiceId).Where(id => !string.IsNullOrEmpty(id)).ToHashSet();

                    _loggingService.LogInformation("Found {StripeInvoiceCount} Stripe invoices and {ExistingPaymentCount} existing payment records",
                        stripeInvoices.Count(), existingPayments.Count);

                    // Step 3: Find missing invoices that need to be processed
                    var missingInvoices = stripeInvoices.Where(invoice => !existingInvoiceIds.Contains(invoice.Id)).ToList();

                    if (!missingInvoices.Any())
                    {
                        _loggingService.LogInformation("No missing payment records found for subscription {StripeSubscriptionId}", stripeSubscriptionId);
                        return new FetchUpdatePaymentResponse
                        {
                            TotalCount = 0,
                            ProcessedCount = 0,
                        };
                    }

                    _loggingService.LogInformation("Found {MissingInvoiceCount} missing payment records to process", missingInvoices.Count);

                    // Step 4: Process each missing invoice
                    int processedCount = 0;
                    foreach (var invoice in missingInvoices)
                    {
                        try
                        {
                            // Extract metadata from the subscription or invoice
                            var metadata = invoice.SubscriptionDetails?.Metadata ?? new Dictionary<string, string>();

                            // Try to get userId and subscriptionId from metadata
                            if (!metadata.TryGetValue("userId", out var userId) || string.IsNullOrEmpty(userId))
                            {
                                _loggingService.LogWarning("Missing userId in invoice {InvoiceId} metadata, skipping", invoice.Id);
                                continue;
                            }

                            if (!metadata.TryGetValue("subscriptionId", out var subscriptionId) || string.IsNullOrEmpty(subscriptionId))
                            {
                                _loggingService.LogWarning("Missing subscriptionId in invoice {InvoiceId} metadata, skipping", invoice.Id);
                                continue;
                            }

                            // Create InvoiceRequest for processing
                            var invoiceRequest = new InvoiceRequest
                            {
                                Id = invoice.Id,
                                Provider = "Stripe",
                                ChargeId = invoice.ChargeId,
                                PaymentIntentId = invoice.PaymentIntentId,
                                UserId = userId,
                                SubscriptionId = subscriptionId,
                                ProviderSubscripitonId = stripeSubscriptionId,
                                Amount = invoice.AmountPaid,
                                Currency = invoice.Currency?.ToUpperInvariant() ?? "USD",
                                Status = MapInvoiceStatusToPaymentStatus(invoice.Status)
                            };

                            // Process the invoice using existing logic
                            var processResult = await ProcessInvoicePaidEvent(invoiceRequest);

                            if (processResult.IsSuccess)
                            {
                                processedCount++;
                                _loggingService.LogInformation("Successfully processed missing invoice {InvoiceId}", invoice.Id);
                            }
                            else
                            {
                                _loggingService.LogWarning("Failed to process invoice {InvoiceId}: {Error}", invoice.Id, processResult.ErrorMessage);
                            }
                        }
                        catch (Exception ex)
                        {
                            _loggingService.LogError("Error processing invoice {InvoiceId}: {Error}", invoice.Id, ex.Message);
                            // Continue processing other invoices even if one fails
                        }
                    }

                    _loggingService.LogInformation("Processed {ProcessedCount} out of {TotalMissingCount} missing payment records",
                        processedCount, missingInvoices.Count);

                    var response = new FetchUpdatePaymentResponse
                    {
                        TotalCount = missingInvoices.Count,
                        ProcessedCount = processedCount,
                    };


                    return response;
                })
                .WithComprehensiveResilience(
                    maxRetries: 3,                      // Moderate retries for payment synchronization
                    timeout: TimeSpan.FromMinutes(2),   // Longer timeout for bulk operations
                    failureRatio: 0.5                   // Higher threshold as this is a batch operation
                )
                .WithPerformanceMonitoring(
                    TimeSpan.FromSeconds(30),   // Warning threshold - this is a bulk operation
                    TimeSpan.FromMinutes(1)     // Error threshold - before overall timeout
                )
                .WithContext("OperationType", "PaymentSynchronization")
                .WithContext("PaymentProvider", "Stripe")
                .WithContext("SubscriptionId", stripeSubscriptionId)
                .OnSuccess(async (syncResult) =>
                {
                    if (syncResult.ProcessedCount > 0)
                    {
                        _loggingService.LogInformation("Successfully synchronized {ProcessedCount} missing payment records for subscription {StripeSubscriptionId}",
                            syncResult.ProcessedCount, stripeSubscriptionId);
                    }
                    else
                    {
                        _loggingService.LogInformation("No payment records needed synchronization for subscription {StripeSubscriptionId}",
                            stripeSubscriptionId);
                    }
                })
                .ExecuteAsync();
        }

        /// <summary>
        /// Searches for Stripe subscriptions by metadata
        /// </summary>
        /// <param name="metadataKey">The metadata key to search for</param>
        /// <param name="metadataValue">The metadata value to search for</param>
        /// <returns>The first matching Stripe subscription ID, or null if not found</returns>
        public async Task<ResultWrapper<string?>> SearchStripeSubscriptionByMetadataAsync(string metadataKey, string metadataValue)
        {
            // Step 1: Input validation first (before resilience wrapper)
            if (string.IsNullOrEmpty(metadataKey))
                throw new ArgumentNullException(nameof(metadataKey), "Metadata key cannot be null or empty");

            if (string.IsNullOrEmpty(metadataValue))
                throw new ArgumentNullException(nameof(metadataValue), "Metadata value cannot be null or empty");

            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Payment",
                    FileName = "PaymentService",
                    OperationName = "SearchStripeSubscriptionByMetadataAsync(string metadataKey, string metadataValue)",
                    State = {
                        ["MetadataKey"] = metadataKey,
                        ["MetadataValue"] = metadataValue
                    },
                    LogLevel = LogLevel.Error // Search operations are important but not critical
                },
                async () =>
                {
                    _loggingService.LogInformation("Searching for Stripe subscription with metadata {MetadataKey}={MetadataValue}",
                        metadataKey, metadataValue);

                    // Search for Stripe subscriptions with the specified metadata
                    var stripeSubscriptions = await _stripeService.SearchSubscriptionsByMetadataAsync(metadataKey, metadataValue);

                    // Return the first matching subscription ID, or null if none found
                    var firstSubscription = stripeSubscriptions?.FirstOrDefault();
                    var subscriptionId = firstSubscription?.Id;

                    if (!string.IsNullOrEmpty(subscriptionId))
                    {
                        _loggingService.LogInformation("Found Stripe subscription {StripeSubscriptionId} for metadata {MetadataKey}={MetadataValue}",
                            subscriptionId, metadataKey, metadataValue);
                    }
                    else
                    {
                        _loggingService.LogInformation("No Stripe subscription found for metadata {MetadataKey}={MetadataValue}",
                            metadataKey, metadataValue);
                    }

                    return subscriptionId;
                })
                .WithHttpResilience() // HTTP resilience for external Stripe API calls
                .WithPerformanceMonitoring(
                    TimeSpan.FromSeconds(3),   // Warning threshold - search operations should be reasonably fast
                    TimeSpan.FromSeconds(10)   // Error threshold - reasonable timeout for external API
                )
                .WithContext("OperationType", "StripeSubscriptionSearch")
                .WithContext("PaymentProvider", "Stripe")
                .WithContext("SearchCriteria", $"{metadataKey}:{metadataValue}")
                .ExecuteAsync();
        }

        public async Task<ResultWrapper> CancelStripeSubscriptionAsync(string stripeSubscriptionId)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Payment",
                    FileName = "PaymentService",
                    OperationName = "CancelStripeSubscription(string stripeSubscriptionId)",
                    State = {
                        ["StripeSubscriptionId"] = stripeSubscriptionId
                    },
                },
                async () =>
                {
                    // Get the Stripe service from payment providers
                    if (!Providers.TryGetValue("Stripe", out var stripeProvider))
                        throw new InvalidOperationException("Stripe payment provider not available");

                    var stripeService = stripeProvider as IStripeService;
                    if (stripeService == null)
                        throw new InvalidOperationException("Stripe service not properly configured");

                    var stripeCancelResult = await stripeService.CancelSubscription(stripeSubscriptionId);

                    if (stripeCancelResult == null || !stripeCancelResult.IsSuccess)
                    {
                        throw new PaymentApiException(
                            $"Failed to cancel Stripe subscription {stripeSubscriptionId}: {stripeCancelResult.ErrorMessage}. " +
                            "Cannot proceed with domain cancellation as user may still be charged.",
                            "Stripe",
                            stripeSubscriptionId);
                    }
                })
                .WithHttpResilience() // For Stripe API calls
                .WithPerformanceMonitoring(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(45)) // Allow more time for Stripe operations
                .OnError(async (ex) =>
                {
                    _loggingService.LogError("❌ CRITICAL ERROR cancelling subscription {SubscriptionId}: {Error}. " +
                        "If this was a Stripe cancellation failure, user may still be charged!",
                        stripeSubscriptionId, ex.Message);
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper> ReactivateStripeSubscriptionAsync(string stripeSubscriptionId)
        {
            throw new NotImplementedException();
        }

        public async Task<ResultWrapper> PauseStripeSubscriptionAsync(string stripeSubscriptionId)
        {
            // Step 1: Input validation
            if (string.IsNullOrEmpty(stripeSubscriptionId))
                throw new ArgumentNullException(nameof(stripeSubscriptionId), "Stripe subscription ID cannot be null or empty");

            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Payment",
                    FileName = "PaymentService",
                    OperationName = "PauseStripeSubscriptionAsync(string stripeSubscriptionId)",
                    State = {
                        ["StripeSubscriptionId"] = stripeSubscriptionId
                    },
                    LogLevel = LogLevel.Critical
                },
                async () =>
                {
                    _loggingService.LogInformation("Pausing Stripe subscription {StripeSubscriptionId}", stripeSubscriptionId);

                    // Get the Stripe service from payment providers
                    if (!Providers.TryGetValue("Stripe", out var stripeProvider))
                        throw new InvalidOperationException("Stripe payment provider not available");

                    var stripeService = stripeProvider as IStripeService;
                    if (stripeService == null)
                        throw new InvalidOperationException("Stripe service not properly configured");

                    // Pause the subscription in Stripe by setting pause_collection
                    var stripePauseResult = await stripeService.PauseSubscriptionAsync(stripeSubscriptionId);

                    if (stripePauseResult == null || !stripePauseResult.IsSuccess)
                    {
                        throw new PaymentApiException(
                            $"Failed to pause Stripe subscription {stripeSubscriptionId}: {stripePauseResult?.ErrorMessage}",
                            "Stripe",
                            stripeSubscriptionId);
                    }

                    _loggingService.LogInformation("Successfully paused Stripe subscription {StripeSubscriptionId}", stripeSubscriptionId);
                })
                .WithHttpResilience() // For Stripe API calls
                .WithPerformanceMonitoring(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30))
                .WithContext("OperationType", "SubscriptionPause")
                .WithContext("PaymentProvider", "Stripe")
                .OnError(async (ex) =>
                {
                    _loggingService.LogError("❌ CRITICAL ERROR pausing Stripe subscription {StripeSubscriptionId}: {Error}",
                        stripeSubscriptionId, ex.Message);
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper> ResumeStripeSubscriptionAsync(string stripeSubscriptionId)
        {
            // Step 1: Input validation
            if (string.IsNullOrEmpty(stripeSubscriptionId))
                throw new ArgumentNullException(nameof(stripeSubscriptionId), "Stripe subscription ID cannot be null or empty");

            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Payment",
                    FileName = "PaymentService",
                    OperationName = "ResumeStripeSubscriptionAsync(string stripeSubscriptionId)",
                    State = {
                        ["StripeSubscriptionId"] = stripeSubscriptionId
                    },
                    LogLevel = LogLevel.Critical
                },
                async () =>
                {
                    _loggingService.LogInformation("Resuming Stripe subscription {StripeSubscriptionId}", stripeSubscriptionId);

                    // Get the Stripe service from payment providers
                    if (!Providers.TryGetValue("Stripe", out var stripeProvider))
                        throw new InvalidOperationException("Stripe payment provider not available");

                    var stripeService = stripeProvider as IStripeService;
                    if (stripeService == null)
                        throw new InvalidOperationException("Stripe service not properly configured");

                    // Resume the subscription in Stripe by removing pause_collection
                    var stripeResumeResult = await stripeService.ResumeSubscriptionAsync(stripeSubscriptionId);

                    if (stripeResumeResult == null || !stripeResumeResult.IsSuccess)
                    {
                        throw new PaymentApiException(
                            $"Failed to resume Stripe subscription {stripeSubscriptionId}: {stripeResumeResult?.ErrorMessage}",
                            "Stripe",
                            stripeSubscriptionId);
                    }

                    _loggingService.LogInformation("Successfully resumed Stripe subscription {StripeSubscriptionId}", stripeSubscriptionId);
                })
                .WithHttpResilience() // For Stripe API calls
                .WithPerformanceMonitoring(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30))
                .WithContext("OperationType", "SubscriptionResume")
                .WithContext("PaymentProvider", "Stripe")
                .OnError(async (ex) =>
                {
                    _loggingService.LogError("❌ CRITICAL ERROR resuming Stripe subscription {StripeSubscriptionId}: {Error}",
                        stripeSubscriptionId, ex.Message);
                })
                .ExecuteAsync();
        }

        public async Task Handle(ExchangeOrderCompletedEvent notification)
        {

        }

        #region Helpers

        /// <summary>
        /// Creates a new failed payment record from Stripe invoice data
        /// </summary>
        private async Task<PaymentData> CreateNewFailedPaymentRecord(PaymentIntentRequest paymentIntentRequest)
        {
            _loggingService.LogInformation("Creating new failed payment record for {PaymentId}", paymentIntentRequest.PaymentId);

            // Get invoice details from Stripe
            var invoice = await _stripeService.GetInvoiceAsync(paymentIntentRequest.InvoiceId);
            if (invoice == null)
                throw new ValidationException($"Invoice {paymentIntentRequest.InvoiceId} not found in Stripe",
                    new Dictionary<string, string[]> { ["InvoiceId"] = new[] { "Invoice not found" } });

            var chargeId = invoice.ChargeId;

            // Calculate payment amounts
            var (total, fee, platformFee, net) = await CalculatePaymentFees(new()
            {
                Provider = "Stripe",
                ChargeId = chargeId,
                PaymentIntentId = paymentIntentRequest.PaymentId,
                UserId = paymentIntentRequest.UserId,
                SubscriptionId = paymentIntentRequest.SubscriptionId,
                Amount = invoice.AmountRemaining,
                Currency = paymentIntentRequest.Currency,
                Status = paymentIntentRequest.Status
            });

            // Create new failed payment record
            var paymentData = new PaymentData
            {
                UserId = Guid.Parse(paymentIntentRequest.UserId),
                SubscriptionId = Guid.Parse(paymentIntentRequest.SubscriptionId),
                Provider = "Stripe",
                PaymentProviderId = paymentIntentRequest.PaymentId, // This should be PaymentId, not SubscriptionId
                InvoiceId = paymentIntentRequest.InvoiceId,
                TotalAmount = paymentIntentRequest.Amount / 100m,
                Currency = paymentIntentRequest.Currency?.ToUpperInvariant() ?? "USD",
                PaymentProviderFee = fee,
                PlatformFee = platformFee,
                NetAmount = net,
                Status = PaymentStatus.Failed,
                FailureReason = paymentIntentRequest.LastPaymentError ?? "Payment failed",
                AttemptCount = 1,
                LastAttemptAt = DateTime.UtcNow
            };

            var insertResult = await InsertAsync(paymentData);
            if (insertResult == null || !insertResult.IsSuccess)
                throw new DatabaseException(insertResult?.ErrorMessage ?? "Failed to insert failed payment record");

            _loggingService.LogInformation("Created new failed payment record {PaymentDataId} for {PaymentId}", paymentData.Id, paymentIntentRequest.PaymentId);
            return paymentData;
        }

        /// <summary>
        /// Updates existing payment record with failure information
        /// </summary>
        private async Task<PaymentData> UpdateExistingPaymentRecord(PaymentData existingPayment, PaymentIntentRequest paymentIntentRequest)
        {
            _loggingService.LogInformation("Updating existing payment record {PaymentDataId} for {PaymentId}", existingPayment.Id, paymentIntentRequest.PaymentId);

            // Update payment with failure details
            existingPayment.Status = PaymentStatus.Failed;
            existingPayment.FailureReason = paymentIntentRequest.LastPaymentError ?? "Payment failed";
            existingPayment.AttemptCount = existingPayment.AttemptCount + 1;
            existingPayment.LastAttemptAt = DateTime.UtcNow;

            var updateResult = await UpdateAsync(existingPayment.Id, existingPayment);
            if (updateResult == null || !updateResult.IsSuccess)
                throw new DatabaseException(updateResult?.ErrorMessage ?? "Failed to update existing payment record");

            _loggingService.LogInformation("Updated existing payment record {PaymentDataId} for {PaymentId}", existingPayment.Id, paymentIntentRequest.PaymentId);
            return existingPayment;
        }

        /// <summary>
        /// Maps Stripe invoice status to our payment status
        /// </summary>
        /// <param name="invoiceStatus">Stripe invoice status</param>
        /// <returns>Our payment status</returns>
        private string MapInvoiceStatusToPaymentStatus(string invoiceStatus)
        {
            return invoiceStatus?.ToLowerInvariant() switch
            {
                "paid" => PaymentStatus.Filled,
                "open" => PaymentStatus.Pending,
                "void" => PaymentStatus.Failed,
                "uncollectible" => PaymentStatus.Failed,
                _ => PaymentStatus.Pending
            };
        }
        private void ValidateCharge(ChargeRequest c)
        {
            if (c == null) throw new ValidationException("Charge is null", new Dictionary<string, string[]>());
            if (string.IsNullOrEmpty(c.PaymentIntentId))
                throw new ValidationException("Missing PaymentIntentId", new Dictionary<string, string[]>());
        }

        private void ValidateInvoiceMetadata(dynamic inv)
        {
            var errs = new List<string>();
            if (!inv.Metadata.TryGetValue("userId", out string uid) || string.IsNullOrEmpty(uid))
                errs.Add("Missing userId");
            if (!inv.Metadata.TryGetValue("subscriptionId", out string sid) || string.IsNullOrEmpty(sid))
                errs.Add("Missing subscriptionId");
            if (errs.Any()) throw new ValidationException("Invalid metadata", errs.ToDictionary(e => "Metadata", e => new[] { e }));
        }

        private void ValidateInvoiceRequest(InvoiceRequest r)
        {
            var errors = new Dictionary<string, List<string>>();
            if (string.IsNullOrWhiteSpace(r.UserId) || !Guid.TryParse(r.UserId, out _))
                AddValidationError(errors, "UserId", "Invalid");
            if (string.IsNullOrWhiteSpace(r.SubscriptionId) || !Guid.TryParse(r.SubscriptionId, out _))
                AddValidationError(errors, "SubscriptionId", "Invalid");
            if (string.IsNullOrWhiteSpace(r.Id))
                AddValidationError(errors, "InvoiceId", "Invalid");
            if (r.Amount <= 0)
                AddValidationError(errors, "Amount", "Must be>0");
            if (errors.Any()) throw new ValidationException("Invoice validation failed", errors.ToDictionary(k => k.Key, k => k.Value.ToArray()));
        }

        private void ValidateCheckoutRequest(CreateCheckoutSessionDto r)
        {
            var errors = new Dictionary<string, List<string>>();
            if (r == null)
                AddValidationError(errors, nameof(r), "Invalid");
            if (string.IsNullOrWhiteSpace(r.SubscriptionId) || !Guid.TryParse(r.SubscriptionId, out _))
                AddValidationError(errors, "SubscriptionId", "Invalid");
            if (string.IsNullOrWhiteSpace(r.UserId) || !Guid.TryParse(r.UserId, out _))
                AddValidationError(errors, "UserId", "Invalid");
            if (r.Amount <= 0)
                AddValidationError(errors, "Amount", "Must be>0");
            if (errors.Any()) throw new ValidationException("Create checkout session request validation failed", errors.ToDictionary(k => k.Key, k => k.Value.ToArray()));
        }

        private void AddValidationError(Dictionary<string, List<string>> errs, string key, string msg)
        {
            if (!errs.TryGetValue(key, out var list)) { list = new(); errs[key] = list; }
            list.Add(msg);
        }

        public async Task<(decimal total, decimal fee, decimal platform, decimal net)> CalculatePaymentFees(InvoiceRequest i)
        {
            var total = i.Amount / 100m;
            var fee = await Providers["stripe"].GetFeeAsync(i.PaymentIntentId);
            var platformFee = total * 0.01m;
            var net = total - fee - platformFee;
            if (net <= 0)
            {
                await _loggingService.LogTraceAsync("Invalid net amount. Amount must be > 0", "CalculatePaymentAmounts");
                throw new ValidationException("Net<=0", new Dictionary<string, string[]> { { "NetAmount", new[] { "Must>0" } } });
            }
            return (total, fee, platformFee, net);
        }

        private CheckoutSessionOptions BuildCheckoutSessionOptions(CreateCheckoutSessionDto r, string customerId, string? corr = null)
        {
            var opts = new CheckoutSessionOptions
            {
                PaymentMethodType = "card",
                Mode = r.IsRecurring ? "subscription" : "payment",
                SuccessUrl = r.ReturnUrl,
                CancelUrl = r.CancelUrl,
                CustomerId = customerId,
                Metadata = new() { ["userId"] = r.UserId, ["subscriptionId"] = r.SubscriptionId },
                LineItems = new List<SessionLineItem> { new() { Currency = r.Currency, UnitAmount = (long)(r.Amount * 100), Name = "Investment", Quantity = 1, Interval = r.Interval } }
            };
            if (!string.IsNullOrEmpty(corr)) opts.Metadata["correlationId"] = corr;
            return opts;
        }

        private async Task SendPaymentReceivedNotification(InvoiceRequest r)
        {
            try
            {
                await _notificationService.CreateAndSendNotificationAsync(new NotificationData
                {
                    UserId = r.UserId,
                    Message = $"A payment {(r.Amount / 100m)} {r.Currency.ToUpper()} is received.",
                    IsRead = false
                });
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning("Notification failed: {ErrorMessage}", ex.Message);
            }
        }

        private async Task SendPaymentFailedNotification(PaymentIntentRequest paymentIntentRequest)
        {
            try
            {
                // Get user details for personalized messaging
                var userResult = await _userService.GetByIdAsync(Guid.Parse(paymentIntentRequest.UserId));
                var userEmail = userResult?.Email ?? 
                    throw new ResourceNotFoundException("User", paymentIntentRequest.UserId);

                // Enhanced notification with actionable information
                await _notificationService.CreateAndSendNotificationAsync(new NotificationData
                {
                    UserId = paymentIntentRequest.UserId,
                    Message = $"Your payment of {(paymentIntentRequest.Amount / 100m):C} {paymentIntentRequest.Currency.ToUpper()} failed. " +
                             $"Reason: {paymentIntentRequest.LastPaymentError ?? "Payment declined"}. " +
                             $"Please update your payment method or contact support.",
                    IsRead = false
                });

                // Send email notification with retry options
                await SendPaymentFailedEmail(paymentIntentRequest, userEmail);

                _loggingService.LogInformation("Payment failure notification sent for user {UserId}, payment {PaymentId}",
                    paymentIntentRequest.UserId, paymentIntentRequest.PaymentId);
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Failed to send payment failure notification: {ErrorMessage}", ex.Message);
            }
        }

        private async Task SendPaymentFailedEmail(PaymentIntentRequest paymentIntentRequest, string userEmail)
        {
            var data = new
            {
                Amount = (paymentIntentRequest.Amount / 100m).ToString("C"),
                Currency = paymentIntentRequest.Currency.ToUpper(),
                FailureReason = paymentIntentRequest.LastPaymentError ?? "Payment was declined by your bank",
                UpdatePaymentMethodUrl = $"{_appSettings.BaseUrl}/dashboard/subscription/{paymentIntentRequest.SubscriptionId}/payment-method",
                RetryPaymentUrl = $"{_appSettings.BaseUrl}/dashboard/subscription/{paymentIntentRequest.SubscriptionId}/retry-payment",
                SupportUrl = $"{_appSettings.BaseUrl}/support"
            };

            var emailTemplate = new EmailMessage
            {
                To = [userEmail],
                Subject = "Payment Failed - Action Required",
                Body = GeneratePaymentFailedEmailHtml(data),
                IsHtml = true,
                
            };

            // Send via your email service
            await _emailService.SendEmailAsync(emailTemplate);
        }

        private string GeneratePaymentFailedEmailHtml(dynamic data)
        {
            return $@"
            <html>
            <body style='font-family: Arial, sans-serif; color: #333;'>
                <div style='max-width: 600px; margin: 0 auto; padding: 20px; border: 1px solid #e1e1e1; border-radius: 5px;'>
                    <div style='text-align: center; margin-bottom: 20px;'>
                        <h2 style='color: #f44336;'>Payment Failed - Action Required</h2>
                    </div>
                    <div style='padding: 20px;'>
                        <p>Hello,</p>
                        <p>We were unable to process your payment of <strong>{data.Amount} {data.Currency}</strong>.</p>
                        
                        <div style='background-color: #fff3e0; padding: 15px; border-radius: 5px; margin: 20px 0; border-left: 4px solid #ff9800;'>
                            <p style='margin: 0; font-weight: bold; color: #e65100;'>Reason for failure:</p>
                            <p style='margin: 5px 0 0 0;'>{data.FailureReason}</p>
                        </div>

                        <p>To continue your subscription and avoid service interruption, please take one of the following actions:</p>

                        <div style='margin: 30px 0;'>
                            <p style='text-align: center; margin-bottom: 15px;'>
                                <a href='{data.RetryPaymentUrl}' style='display: inline-block; padding: 12px 24px; background-color: #2196F3; color: white; text-decoration: none; border-radius: 5px; font-weight: bold;'>Retry Payment</a>
                                <a href='{data.UpdatePaymentMethodUrl}' style='display: inline-block; padding: 12px 24px; background-color: #4CAF50; color: white; text-decoration: none; border-radius: 5px; font-weight: bold; margin-right: 10px;'>Update Payment Method</a>
                            </p>
                        </div>

                        <p><strong>What you can do:</strong></p>
                        <ul>
                            <li>Check that your payment method has sufficient funds</li>
                            <li>Verify that your payment information is up to date</li>
                            <li>Contact your bank if the issue persists</li>
                            <li>Try using a different payment method</li>
                        </ul>

                        <p>If you continue to experience issues, please don't hesitate to <a href='{data.SupportUrl}' style='color: #4CAF50; text-decoration: none;'>contact our support team</a> for assistance.</p>
                        
                        <p>Thank you,<br>Crypto Investment Platform Team</p>
                    </div>
                    <div style='margin-top: 20px; border-top: 1px solid #e1e1e1; padding-top: 20px; font-size: 12px; color: #777; text-align: center;'>
                        <p>This is an automated message, please do not reply to this email.</p>
                        <p>If you did not attempt this payment, please contact our support team immediately.</p>
                    </div>
                </div>
            </body>
            </html>";
        }

        private string MapStripeStatusToLocal(string s)
            => s switch
            {
                "succeeded" => PaymentStatus.Filled,
                "canceled" => PaymentStatus.Failed,
                _ => PaymentStatus.Pending
            };

        private IPaymentProvider GetProvider(string? name)
            => !string.IsNullOrEmpty(name) && _providers.TryGetValue(name, out var p) ? p : _defaultProvider;

        private IPaymentProvider GetProviderForPaymentId(string pid)
            => pid.StartsWith("pi_") ? _providers["Stripe"] : _defaultProvider;

        private async Task<PaymentData?> GetPaymentDataForCancel(string pid)
        {
            if (Guid.TryParse(pid, out var guid) && guid != Guid.Empty)
            {
                var wr = await GetByIdAsync(guid);
                return wr.Data;
            }
            var wr2 = await GetOneAsync(Builders<PaymentData>.Filter.Eq(p => p.PaymentProviderId, pid));
            return wr2.Data;
        }

        private async Task<PaymentDetailsDto?> GetStripePaymentDetailsAsync(string pid)
        {
            var stripe = _stripeService;
            var pi = await stripe!.GetPaymentIntentAsync(pid);
            if (pi == null) return null;
            return new PaymentDetailsDto
            {
                Id = pid,
                UserId = pi.Metadata.TryGetValue("userId", out var u) ? u : "",
                SubscriptionId = pi.Metadata.TryGetValue("subscriptionId", out var s) ? s : "",
                Provider = "Stripe",
                PaymentProviderId = pid,
                TotalAmount = pi.Amount / 100m,
                PaymentProviderFee = 0m,
                PlatformFee = pi.Amount / 100m * 0.01m,
                NetAmount = pi.Amount / 100m * 0.99m,
                Currency = pi.Currency?.ToUpperInvariant() ?? "USD",
                Status = MapStripeStatusToLocal(pi.Status),
                CreatedAt = pi.Created
            };
        }

        private async Task<string> GetOrCreateStripeCustomerAsync(string userId, string email)
        {
            if (!Guid.TryParse(userId, out var uid)) throw new ArgumentException("Invalid userId");
            var userData = await _userService.GetByIdAsync(uid);
            var existingCustomer = userData?.PaymentProviderCustomerId;

            var isCustomerValid = !string.IsNullOrEmpty(existingCustomer) && await _stripeService.CheckCustomerExists(existingCustomer);

            if (isCustomerValid) return existingCustomer!;

            var stripeProvider = _stripeService;
            var search = await stripeProvider.SearchCustomersAsync(new() { ["query"] = $"email:'{email}'" });
            if (search != null && search.Any())
            {
                var cid = search.First().Id;
                await _userService.UpdateAsync(uid, new UserUpdateDTO { PaymentProviderCustomerId = cid });
                return cid;
            }

            var newCust = await stripeProvider.CreateCustomerAsync(new() { ["email"] = email, ["metadata"] = new Dictionary<string, string> { { "userId", userId } } });
            if (newCust == null) throw new PaymentApiException("Failed create customer", "Stripe");
            await _userService.UpdateAsync(uid, new UserUpdateDTO { PaymentProviderCustomerId = newCust.Id });
            return newCust.Id;
        }

        private bool IsCancellable(string status)
        {
            // Only payments in certain states can be cancelled
            return status == PaymentStatus.Pending ||
                   status == PaymentStatus.Queued;
        }

        #endregion
    }
}
