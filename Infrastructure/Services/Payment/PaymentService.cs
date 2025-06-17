using Application.Contracts.Requests.Payment;
using Application.Interfaces;
using Application.Interfaces.Asset;
using Application.Interfaces.Base;
using Application.Interfaces.Logging;
using Application.Interfaces.Payment;
using Domain.Constants;
using Domain.Constants.Logging;
using Domain.Constants.Payment;
using Domain.DTOs;
using Domain.DTOs.Payment;
using Domain.DTOs.Settings;
using Domain.DTOs.User;
using Domain.Events;
using Domain.Exceptions;
using Domain.Models.Payment;
using Domain.Models.User;
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
        private readonly IStripeService _stripeService;
        private readonly IAssetService _assetService;
        private readonly IUserService _userService;
        private readonly INotificationService _notificationService;
        private readonly IIdempotencyService _idempotencyService;

        public IReadOnlyDictionary<string, IPaymentProvider> Providers => _providers;
        public IPaymentProvider DefaultProvider => _defaultProvider;

        public PaymentService(
            ICrudRepository<PaymentData> repository,
            ICacheService<PaymentData> cacheService,
            IMongoIndexService<PaymentData> indexService,
            ILoggingService logger,
            IEventService eventService,
            IOptions<PaymentServiceSettings> paymentSettings,
            IOptions<StripeSettings> stripeSettings,
            IAssetService assetService,
            IUserService userService,
            INotificationService notificationService,
            IIdempotencyService idempotencyService
        ) : base(repository, cacheService, indexService, logger, eventService)
        {
            _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _idempotencyService = idempotencyService ?? throw new ArgumentNullException(nameof(idempotencyService));
            _stripeService = new StripeService(stripeSettings);
            _providers.Add("Stripe", _stripeService as IPaymentProvider);
            _defaultProvider = _providers[
                paymentSettings.Value.DefaultProvider
                    ?? throw new InvalidOperationException("DefaultProvider not configured")];
        }

        public async Task<ResultWrapper> ProcessInvoicePaidEvent(InvoiceRequest invoice)
        {
            using var scope = Logger.BeginScope("ProcessInvoicePaidEvent", new Dictionary<string, object?>
            {
                ["InvoiceId"] = invoice?.Id,
            });

            try
            {
                ValidateInvoiceRequest(invoice);

                var key = $"invoice-paid-{invoice.Id}";
                var (hit, _) = await _idempotencyService.GetResultAsync<Guid>(key);
                if (hit)
                {
                    Logger.LogWarning($"Invoice {invoice.Id} already processed.");
                    return ResultWrapper.Success($"Invoice {invoice.Id} already processed.");
                }

                // Check existing
                var existingWr = await GetOneAsync(
                    Builders<PaymentData>.Filter.Eq(p => p.InvoiceId, invoice.Id));

                if (existingWr.Data != null)
                {
                    Logger.LogWarning($"Invoice data with ID {invoice.Id} already present.");
                    return ResultWrapper.Success($"Invoice data with ID {invoice.Id} already present.");
                }

                // Calculate amounts
                var (total, fee, platformFee, net) = await CalculatePaymentAmounts(invoice);

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

                if (!insertWr.IsSuccess)
                    throw new DatabaseException(insertWr.ErrorMessage ?? "Insert failed");

                await EventService!.PublishAsync(new PaymentReceivedEvent(paymentData, Logger.Context));

                await SendPaymentReceivedNotification(invoice);

                await _idempotencyService.StoreResultAsync(key, paymentData.Id);
                return ResultWrapper.Success();
            }
            catch (Exception ex)
            {
                Logger.LogError($"Invoice paid processing failed: {ex.Message}");
                return ResultWrapper.FromException(ex);
            }
        }

        public async Task<ResultWrapper> ProcessCheckoutSessionCompletedAsync(SessionDto session)
        {
            using var scope = Logger.BeginScope("PaymentService::ProcessCheckoutSessionCompletedAsync", new Dictionary<string, object?>
            {
                ["SessionId"] = session?.Id,
            });
            try
            {
                await EventService!.PublishAsync(new CheckoutSessionCompletedEvent(session, Logger.Context));
                return ResultWrapper.Success();
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to process checkout.session.completed event: {ex.Message}");
                return ResultWrapper.FromException(ex);
            }
        }

        public async Task<ResultWrapper<SessionDto>> CreateCheckoutSessionAsync(CreateCheckoutSessionDto request)
        {
            using var scope = Logger.BeginScope("PaymentService::CreateCheckoutSessionAsync", new Dictionary<string, object?>
            {
                ["UserId"] = request.UserId,
                ["SubscriptionId"] = request.SubscriptionId,
            });

            try
            {
                ValidateCheckoutRequest(request);

                var provider = GetProvider(request.Provider);
                var customerId = await GetOrCreateStripeCustomerAsync(
                    request.UserId, request.UserEmail);

                var options = BuildCheckoutSessionOptions(request, customerId);
                var sessionWr = await provider.CreateCheckoutSessionWithOptions(options);
                if (!sessionWr.IsSuccess || sessionWr.Data == null)
                    throw new PaymentApiException("Failed to create session", provider.Name);

                await EventService!.PublishAsync(new CheckoutSessionCreatedEvent(sessionWr.Data, Logger.Context));
                return ResultWrapper<SessionDto>.Success(sessionWr.Data);
            }
            catch (ValidationException ex)
            {
                Logger.LogError($"Failed to validate create checkout session: {ex.Message}");
                return ResultWrapper<SessionDto>.FromException(ex);
            }
            catch (PaymentApiException ex)
            {
                Logger.LogError($"Payment API Exception: {ex.Message}");
                return ResultWrapper<SessionDto>.FromException(ex);
            }
            catch (Exception ex)
            {
                await Logger.LogTraceAsync(
                    $"Failed to create checkout session: {ex.Message}",
                    requiresResolution: true);
                return ResultWrapper<SessionDto>.FromException(ex);
            }
        }

        public async Task<PaymentStatusResponse> GetPaymentStatusAsync(string paymentId)
        {
            if (string.IsNullOrWhiteSpace(paymentId))
                throw new ArgumentException("Payment ID is required", nameof(paymentId));

            using var scope = Logger.BeginScope("PaymentService::GetPaymentStatusAsync", new Dictionary<string, object?>
            {
                ["PaymentId"] = paymentId,
            });

            try
            {
                var filter = Builders<PaymentData>.Filter.Eq(p => p.PaymentProviderId, paymentId);
                var dbWr = await GetOneAsync(filter);
                if (dbWr.Data != null)
                {
                    var p = dbWr.Data;
                    return new PaymentStatusResponse
                    {
                        Id = p.Id.ToString(),
                        Status = p.Status,
                        Amount = p.TotalAmount,
                        Currency = p.Currency,
                        SubscriptionId = p.SubscriptionId.ToString(),
                        CreatedAt = p.CreatedAt
                    };
                }

                var pi = await (_stripeService as IStripeService)!.GetPaymentIntentAsync(paymentId);
                if (pi != null)
                {
                    return new PaymentStatusResponse
                    {
                        Id = paymentId,
                        Status = MapStripeStatusToLocal(pi.Status),
                        Amount = pi.Amount / 100m,
                        Currency = pi.Currency?.ToUpperInvariant() ?? "USD",
                        SubscriptionId = pi.Metadata.TryGetValue("subscriptionId", out var sid) ? sid : "",
                        CreatedAt = pi.Created,
                        UpdatedAt = DateTime.UtcNow
                    };
                }

                throw new KeyNotFoundException($"Payment {paymentId} not found");
            }
            catch (Exception ex)
            {
                await Logger.LogTraceAsync($"Failed to get payment status: {ex.Message}");
                throw;
            }
        }

        public async Task<PaymentDetailsDto> GetPaymentDetailsAsync(string paymentId)
        {
            using var scope = Logger.BeginScope("PaymentService::GetPaymentDetailsAsync",
                new Dictionary<string, object?>
                {
                    ["PaymentId"] = paymentId,
                });

            if (string.IsNullOrWhiteSpace(paymentId))
                throw new ArgumentException("Payment ID is required", nameof(paymentId));

            try
            {
                if (Guid.TryParse(paymentId, out var guid) && guid != Guid.Empty)
                {
                    var wr = await GetByIdAsync(guid);
                    if (wr != null && wr.IsSuccess && wr.Data != null)
                        return new PaymentDetailsDto(wr.Data);
                }
                else
                {
                    var dbWr = await GetOneAsync(
                        Builders<PaymentData>.Filter.Eq(p => p.PaymentProviderId, paymentId));
                    if (dbWr != null && dbWr.IsSuccess && dbWr.Data != null)
                        return new PaymentDetailsDto(dbWr.Data);

                    var provider = GetProviderForPaymentId(paymentId);
                    if (provider.Name == "Stripe")
                    {
                        var dto = await GetStripePaymentDetailsAsync(paymentId);
                        if (dto != null) return dto;
                    }
                }

                throw new KeyNotFoundException($"Payment details not found for {paymentId}");
            }
            catch (Exception ex)
            {
                await Logger.LogTraceAsync($"Failed to get payment {paymentId} details: {ex.Message}");
                throw;
            }
        }
        public async Task<ResultWrapper<PaymentData>> GetByProviderIdAsync(string paymentProviderId)
        {
            return await SafeExecute(
                async () =>
                {
                    var paymentResult = await GetOneAsync(new FilterDefinitionBuilder<PaymentData>().Eq(t => t.PaymentProviderId, paymentProviderId));
                    if (paymentResult == null || !paymentResult.IsSuccess)
                        throw new KeyNotFoundException("Failed to fetch payment transactions");
                    return paymentResult.Data;
                }
                );
        }

        public async Task<ResultWrapper> CancelPaymentAsync(string paymentId)
        {
            using var scope = Logger.BeginScope(new Dictionary<string, object?>
            {
                ["PaymentId"] = paymentId,
            });
            try
            {
                var key = $"payment-cancelled-{paymentId}";
                var (hit, _) = await _idempotencyService.GetResultAsync<Guid>(key);
                if (hit)

                    return ResultWrapper.Success();

                if (string.IsNullOrEmpty(paymentId))
                    throw new ArgumentException("Payment ID is required", nameof(paymentId));
                // fetch existing payment
                var paymentData = await GetPaymentDataForCancel(paymentId)
                                  ?? throw new KeyNotFoundException($"Payment {paymentId} not found");

                if (!IsCancellable(paymentData.Status))
                    throw new InvalidOperationException($"Cannot cancel payment in status {paymentData.Status}");

                var provider = GetProviderForPaymentId(paymentId);
                var cancelWr = await provider.CancelPaymentAsync(paymentId);
                if (!cancelWr.IsSuccess)
                    throw new PaymentApiException(cancelWr.ErrorMessage ?? "Cancel failed", provider.Name, paymentId);

                var updateWr = await UpdateAsync(paymentData.Id, new { Status = PaymentStatus.Failed });
                if (!updateWr.IsSuccess)
                    throw new DatabaseException(updateWr.ErrorMessage);

                await EventService!.PublishAsync(new PaymentCancelledEvent(paymentData, Logger.Context));
                await _idempotencyService.StoreResultAsync(key, paymentData.Id);
                return ResultWrapper.Success();
            }
            catch (Exception ex)
            {
                Logger.LogError("CancelPayment failed");
                return ResultWrapper.FromException(ex);
            }
        }

        public async Task<ResultWrapper> UpdatePaymentRetryInfoAsync(
            Guid paymentId,
            int attemptCount,
            DateTime lastAttemptAt,
            DateTime nextRetryAt,
            string failureReason)
        {
            using var scope = Logger.BeginScope("PaymentService::UpdatePaymentRetryInfoAsync", new Dictionary<string, object?>
            {
                ["PaymentId"] = paymentId,
                ["AttemptCount"] = attemptCount,
                ["NextRetryAt"] = nextRetryAt
            });

            try
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
                if (!updateResult.IsSuccess)
                    throw new DatabaseException(updateResult.ErrorMessage);

                return ResultWrapper.Success();
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to update payment retry info: {Message}", ex.Message);
                return ResultWrapper.FromException(ex);
            }
        }

        public async Task<ResultWrapper<IEnumerable<PaymentData>>> GetPendingRetriesAsync()
        {
            using var scope = Logger.BeginScope("PaymentService::GetPendingRetriesAsync");

            try
            {
                var now = DateTime.UtcNow;

                // Find payments with: status=failed, nextRetryAt <= now, attemptCount < max attempts
                var filter = Builders<PaymentData>.Filter.And(
                    Builders<PaymentData>.Filter.Eq(p => p.Status, PaymentStatus.Failed),
                    Builders<PaymentData>.Filter.Lte(p => p.NextRetryAt, now),
                    Builders<PaymentData>.Filter.Lt(p => p.AttemptCount, 3) // Max attempts
                );

                var pendingRetries = await GetManyAsync(filter);
                return ResultWrapper<IEnumerable<PaymentData>>.Success(pendingRetries.Data);
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to get pending retries: {Message}", ex.Message);
                return ResultWrapper<IEnumerable<PaymentData>>.FromException(ex);
            }
        }

        public async Task<ResultWrapper> RetryPaymentAsync(Guid paymentId)
        {
            using var scope = Logger.BeginScope("PaymentService::RetryPaymentAsync", new Dictionary<string, object?>
            {
                ["PaymentId"] = paymentId
            });

            try
            {
                // Get payment data
                var paymentResult = await GetByIdAsync(paymentId);
                if (!paymentResult.IsSuccess || paymentResult.Data == null)
                    throw new KeyNotFoundException($"Payment {paymentId} not found");

                var payment = paymentResult.Data;

                // We already have the subscription ID in the payment data
                // We don't need to fetch the subscription anymore
                var provider = GetProvider(payment.Provider);

                // Just retry the payment using the stored provider information
                // You might need to store the provider subscription ID in the PaymentData model
                var retryResult = await _stripeService.RetryPaymentAsync(
                    payment.PaymentProviderId,
                    payment.ProviderSubscriptionId); // You'll need to add this field to PaymentData

                if (!retryResult.IsSuccess)
                    throw new PaymentApiException(retryResult.ErrorMessage ?? "Payment retry failed", provider.Name);

                // Update payment status to pending for webhook to catch
                await UpdateAsync(paymentId, new { Status = PaymentStatus.Pending });

                Logger.LogInformation("Successfully initiated retry for payment {PaymentId}", paymentId);
                return ResultWrapper.Success();
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to retry payment {PaymentId}: {Message}", paymentId, ex.Message);
                return ResultWrapper.FromException(ex);
            }
        }

        public async Task<ResultWrapper> ProcessPaymentFailedAsync(PaymentIntentRequest paymentIntentRequest)
        {
            Logger.BeginScope(nameof(ProcessPaymentFailedAsync), new
            {
                PaymentProviderId = paymentIntentRequest.PaymentId,
                UserId = paymentIntentRequest.UserId,
                SubscriptionId = paymentIntentRequest.SubscriptionId
            });

            try
            {
                // Verify if we have a payment record for this
                var existingPayment = await GetByProviderIdAsync(paymentIntentRequest.InvoiceId);

                PaymentData paymentData;

                if (existingPayment == null || !existingPayment.IsSuccess || existingPayment.Data == null)
                {
                    var invoice = await _stripeService.GetInvoiceAsync(paymentIntentRequest.InvoiceId);
                    var chargeId = invoice.ChargeId;
                    // Calculate amounts
                    var (total, fee, platformFee, net) = await CalculatePaymentAmounts(new()
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

                    // Create a new failed payment record
                    paymentData = new PaymentData
                    {
                        UserId = Guid.Parse(paymentIntentRequest.UserId),
                        SubscriptionId = Guid.Parse(paymentIntentRequest.SubscriptionId),
                        Provider = "Stripe",
                        PaymentProviderId = paymentIntentRequest.SubscriptionId,
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

                    var insertReult = await InsertAsync(paymentData);

                    if (insertReult == null || !insertReult.IsSuccess)
                        await Logger.LogTraceAsync(insertReult?.ErrorMessage ?? "Failed to insert PaymentData",
                            level: LogLevel.Error);
                }
                else
                {
                    // Update existing payment
                    paymentData = existingPayment.Data;
                    paymentData.Status = PaymentStatus.Failed;
                    paymentData.FailureReason = paymentIntentRequest.LastPaymentError ?? "Payment failed";
                    paymentData.AttemptCount = (paymentData.AttemptCount) + 1;
                    paymentData.LastAttemptAt = DateTime.UtcNow;

                    var updateReult = await UpdateAsync(paymentData.Id, paymentData);

                    if (updateReult == null || !updateReult.IsSuccess)
                        await Logger.LogTraceAsync(updateReult?.ErrorMessage ?? "Failed to update PaymentData",
                            level: LogLevel.Error);
                }

                // Publish event for payment failure handling
                await EventService!.PublishAsync(new SubscriptionPaymentFailedEvent(
                    paymentData,
                    paymentData.FailureReason ?? "Payment failed",
                    paymentData.AttemptCount,
                    Logger.Context
                ));

                await SendPaymentFailedNotification(paymentIntentRequest);

                return ResultWrapper.Success();
            }
            catch (Exception ex)
            {
                await Logger.LogTraceAsync(ex.Message, level: LogLevel.Error);
                return ResultWrapper.FromException(ex);
            }
        }
        public async Task<ResultWrapper> ProcessSetupIntentSucceededAsync(Guid subscriptionId)
        {
            Logger.BeginScope("PaymentService::ProcessSetupIntentSucceeded", new
            {
                SubscriptionId = subscriptionId
            });

            try
            {
                // Instead of directly checking subscription status, publish an event
                await EventService.PublishAsync(new PaymentMethodUpdatedEvent(subscriptionId, "", Logger.Context));

                // The rest is handled by event handlers in SubscriptionService
                return ResultWrapper.Success();
            }
            catch (Exception ex)
            {
                Logger.LogError("Error handling setup_intent.succeeded event: {ErrorMessage}", ex.Message);
                return ResultWrapper.FromException(ex);
            }
        }

        public async Task<ResultWrapper<string>> CreateUpdatePaymentMethodSessionAsync(string userId, string subscriptionId)
        {
            using var scope = Logger.BeginScope("PaymentService::CreateUpdatePaymentMethodSession", new Dictionary<string, object?>
            {
                ["UserId"] = userId,
                ["SubscriptionId"] = subscriptionId,
            });

            try
            {
                if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out _))
                    throw new ArgumentException("Invalid userId", nameof(userId));

                if (string.IsNullOrEmpty(subscriptionId) || !Guid.TryParse(subscriptionId, out var parsedSubscriptionId))
                    throw new ArgumentException("Invalid subscriptionId", nameof(subscriptionId));

                // Get the payment data for this subscription to find the provider subscription ID
                var filter = Builders<PaymentData>.Filter.Eq(p => p.SubscriptionId, parsedSubscriptionId);
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
                    await Logger.LogTraceAsync($"Failed to create update payment method session: {sessionResult.ErrorMessage ?? "Session result returned null"}",
                        level: LogLevel.Error,
                        requiresResolution: true);
                    throw new PaymentApiException("Failed to create update payment method session", "Stripe");
                }

                return ResultWrapper<string>.Success(sessionResult.Data.Url);
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to create update payment method session: {Message}", ex.Message);
                return ResultWrapper<string>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<IEnumerable<PaymentData>>> GetPaymentsForSubscriptionAsync(Guid subscriptionId)
        {
            using var scope = Logger.BeginScope("PaymentService::GetPaymentsForSubscription", new Dictionary<string, object?>
            {
                ["SubscriptionId"] = subscriptionId
            });

            try
            {
                var filter = Builders<PaymentData>.Filter.Eq(p => p.SubscriptionId, subscriptionId);
                var sort = Builders<PaymentData>.Sort.Descending(p => p.CreatedAt);

                // You'll need to add a GetSortedAsync method to your base service
                var payments = await GetManyAsync(filter);
                if (!payments.IsSuccess)
                    throw new DatabaseException(payments.ErrorMessage);

                // Sort the payments by date (most recent first)
                var sortedPayments = payments.Data.OrderByDescending(p => p.CreatedAt);

                return ResultWrapper<IEnumerable<PaymentData>>.Success(sortedPayments);
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to get payments for subscription {SubscriptionId}: {Message}",
                    subscriptionId, ex.Message);
                return ResultWrapper<IEnumerable<PaymentData>>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<int>> GetFailedPaymentCountAsync(Guid subscriptionId)
        {
            using var scope = Logger.BeginScope("PaymentService::GetFailedPaymentCount", new Dictionary<string, object?>
            {
                ["SubscriptionId"] = subscriptionId
            });

            try
            {
                var filter = Builders<PaymentData>.Filter.And(
                    Builders<PaymentData>.Filter.Eq(p => p.SubscriptionId, subscriptionId),
                    Builders<PaymentData>.Filter.Eq(p => p.Status, PaymentStatus.Failed)
                );

                var count = await _repository.CountAsync(filter);
                return ResultWrapper<int>.Success((int)count);
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to get failed payment count for subscription {SubscriptionId}: {Message}",
                    subscriptionId, ex.Message);
                return ResultWrapper<int>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<PaymentData>> GetLatestPaymentAsync(Guid subscriptionId)
        {
            using var scope = Logger.BeginScope("PaymentService::GetLatestPayment", new Dictionary<string, object?>
            {
                ["SubscriptionId"] = subscriptionId
            });

            try
            {
                var filter = Builders<PaymentData>.Filter.Eq(p => p.SubscriptionId, subscriptionId);
                var sort = Builders<PaymentData>.Sort.Descending(p => p.CreatedAt);

                // Use the repository to get the most recent payment
                var latestPayment = await _repository.GetOneAsync(filter, sort);

                if (latestPayment == null)
                    return ResultWrapper<PaymentData>.Failure(FailureReason.ResourceNotFound,
                        $"No payments found for subscription {subscriptionId}");

                return ResultWrapper<PaymentData>.Success(latestPayment);
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to get latest payment for subscription {SubscriptionId}: {Message}",
                    subscriptionId, ex.Message);
                return ResultWrapper<PaymentData>.FromException(ex);
            }
        }
        public async Task<ResultWrapper<int>> FetchPaymentsBySubscriptionAsync(string stripeSubscriptionId)
        {
            using var scope = Logger.BeginScope("PaymentService::FetchPaymentsBySubscriptionAsync", new Dictionary<string, object?>
            {
                ["StripeSubscriptionId"] = stripeSubscriptionId
            });

            try
            {
                if (string.IsNullOrEmpty(stripeSubscriptionId))
                {
                    throw new ArgumentNullException(nameof(stripeSubscriptionId), "Stripe subscription ID cannot be null or empty");
                }

                // Step 1: Get all invoices from Stripe for this subscription
                var stripeInvoices = await _stripeService.GetSubscriptionInvoicesAsync(stripeSubscriptionId);

                if (stripeInvoices == null || !stripeInvoices.Any())
                {
                    Logger.LogInformation("No paid invoices found in Stripe for subscription {StripeSubscriptionId}", stripeSubscriptionId);
                    return ResultWrapper<int>.Success(0);
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

                Logger.LogInformation("Found {StripeInvoiceCount} Stripe invoices and {ExistingPaymentCount} existing payment records",
                    stripeInvoices.Count(), existingPayments.Count);

                // Step 3: Find missing invoices that need to be processed
                var missingInvoices = stripeInvoices.Where(invoice => !existingInvoiceIds.Contains(invoice.Id)).ToList();

                if (!missingInvoices.Any())
                {
                    Logger.LogInformation("No missing payment records found for subscription {StripeSubscriptionId}", stripeSubscriptionId);
                    return ResultWrapper<int>.Success(0);
                }

                Logger.LogInformation("Found {MissingInvoiceCount} missing payment records to process", missingInvoices.Count);

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
                            Logger.LogWarning("Missing userId in invoice {InvoiceId} metadata, skipping", invoice.Id);
                            continue;
                        }

                        if (!metadata.TryGetValue("subscriptionId", out var subscriptionId) || string.IsNullOrEmpty(subscriptionId))
                        {
                            Logger.LogWarning("Missing subscriptionId in invoice {InvoiceId} metadata, skipping", invoice.Id);
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
                            Logger.LogInformation("Successfully processed missing invoice {InvoiceId}", invoice.Id);
                        }
                        else
                        {
                            Logger.LogWarning("Failed to process invoice {InvoiceId}: {Error}", invoice.Id, processResult.ErrorMessage);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError("Error processing invoice {InvoiceId}: {Error}", invoice.Id, ex.Message);
                        // Continue processing other invoices even if one fails
                    }
                }

                Logger.LogInformation("Processed {ProcessedCount} out of {TotalMissingCount} missing payment records",
                    processedCount, missingInvoices.Count);

                return ResultWrapper<int>.Success(processedCount);
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to fetch payments by subscription {StripeSubscriptionId}: {Message}",
                    stripeSubscriptionId, ex.Message);
                return ResultWrapper<int>.FromException(ex);
            }
        }

        /// <summary>
        /// Searches for Stripe subscriptions by metadata
        /// </summary>
        /// <param name="metadataKey">The metadata key to search for</param>
        /// <param name="metadataValue">The metadata value to search for</param>
        /// <returns>The first matching Stripe subscription ID, or null if not found</returns>
        public async Task<ResultWrapper<string?>> SearchStripeSubscriptionByMetadataAsync(string metadataKey, string metadataValue)
        {
            using var scope = Logger.BeginScope("PaymentService::SearchStripeSubscriptionByMetadataAsync", new Dictionary<string, object?>
            {
                ["MetadataKey"] = metadataKey,
                ["MetadataValue"] = metadataValue
            });

            try
            {
                if (string.IsNullOrEmpty(metadataKey))
                {
                    throw new ArgumentNullException(nameof(metadataKey), "Metadata key cannot be null or empty");
                }

                if (string.IsNullOrEmpty(metadataValue))
                {
                    throw new ArgumentNullException(nameof(metadataValue), "Metadata value cannot be null or empty");
                }

                // Search for Stripe subscriptions with the specified metadata
                var stripeSubscriptions = await _stripeService.SearchSubscriptionsByMetadataAsync(metadataKey, metadataValue);

                // Return the first matching subscription ID, or null if none found
                var firstSubscription = stripeSubscriptions?.FirstOrDefault();
                var subscriptionId = firstSubscription?.Id;

                if (!string.IsNullOrEmpty(subscriptionId))
                {
                    Logger.LogInformation("Found Stripe subscription {StripeSubscriptionId} for metadata {MetadataKey}={MetadataValue}",
                        subscriptionId, metadataKey, metadataValue);
                }
                else
                {
                    Logger.LogInformation("No Stripe subscription found for metadata {MetadataKey}={MetadataValue}",
                        metadataKey, metadataValue);
                }

                return ResultWrapper<string?>.Success(subscriptionId);
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to search Stripe subscription by metadata {MetadataKey}={MetadataValue}: {Message}",
                    metadataKey, metadataValue, ex.Message);
                return ResultWrapper<string?>.FromException(ex);
            }
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

        #region Helpers
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

        public async Task<(decimal total, decimal fee, decimal platform, decimal net)> CalculatePaymentAmounts(InvoiceRequest i)
        {
            var total = i.Amount / 100m;
            var fee = await Providers["stripe"].GetFeeAsync(i.PaymentIntentId);
            var platformFee = total * 0.01m;
            var net = total - fee - platformFee;
            if (net <= 0)
            {
                await Logger.LogTraceAsync("Invalid net amount. Amount must be > 0", "CalculatePaymentAmounts");
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
                    Message = $"Payment {(r.Amount / 100m)} {r.Currency} received.",
                    IsRead = false
                });
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Notification failed: {ErrorMessage}", ex.Message);
            }
        }

        private async Task SendPaymentFailedNotification(PaymentIntentRequest r)
        {
            try
            {
                await _notificationService.CreateAndSendNotificationAsync(new NotificationData
                {
                    UserId = r.UserId,
                    Message = $"Payment {(r.Amount / 100m)} {r.Currency} failed.",
                    IsRead = false
                });
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Notification failed: {ErrorMessage}", ex.Message);
            }
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
            var wrUser = await _userService.GetByIdAsync(uid);
            var existingCustomer = wrUser.Data?.PaymentProviderCustomerId;
            if (!string.IsNullOrEmpty(existingCustomer)) return existingCustomer;

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
            await _userService.UpdateAsync(uid, new UserData { PaymentProviderCustomerId = newCust.Id });
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
