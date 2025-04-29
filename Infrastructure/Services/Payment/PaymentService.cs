using Application.Contracts.Requests.Payment;
using Application.Interfaces;
using Application.Interfaces.Asset;
using Application.Interfaces.Base;
using Application.Interfaces.Logging;
using Application.Interfaces.Payment;
using Domain.Constants.Payment;
using Domain.DTOs;
using Domain.DTOs.Payment;
using Domain.DTOs.Settings;
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

        private const string CACHE_KEY_SUBSCRIPTION_TRANSACTIONS = "subscription_transactions:{0}";
        private const string CACHE_KEY_PAYMENT_TRANSACTIONS = "payment_transactions:{0}";

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

                await SendPaymentNotification(invoice);

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
            var cacheKey = string.Format(CACHE_KEY_PAYMENT_TRANSACTIONS, paymentProviderId);
            return await FetchCached(
                cacheKey,
                async () =>
                {
                    var paymentResult = await GetOneAsync(new FilterDefinitionBuilder<PaymentData>().Eq(t => t.PaymentProviderId, paymentProviderId));
                    if (paymentResult == null || !paymentResult.IsSuccess)
                        throw new KeyNotFoundException("Failed to fetch payment transactions");
                    return paymentResult.Data;
                },
                TimeSpan.FromHours(1),
                () => new KeyNotFoundException("Failed to fetch payment {paymentProviderId} transactions.")
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

        private async Task<(decimal total, decimal fee, decimal platform, decimal net)> CalculatePaymentAmounts(InvoiceRequest i)
        {
            var total = i.Amount / 100m;
            var fee = 0m;
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

        private async Task SendPaymentNotification(InvoiceRequest r)
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
                await _userService.UpdateAsync(uid, new UserData { PaymentProviderCustomerId = cid });
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
