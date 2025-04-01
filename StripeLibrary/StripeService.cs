using Application.Interfaces;
using Domain.DTOs.Settings;
using Domain.Exceptions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using Stripe;
using Stripe.Checkout;
using System.Diagnostics;

namespace StripeLibrary
{
    /// <summary>
    /// Service for interacting with the Stripe payment platform.
    /// Provides methods for retrieving payment information, fees, and subscription details.
    /// </summary>
    public class StripeService : IPaymentProvider, IStripeService
    {
        private readonly ChargeService _chargeService;
        private readonly BalanceTransactionService _balanceTransactionService;
        private readonly PaymentIntentService _paymentIntentService;
        private readonly InvoiceService _invoiceService;
        private readonly SubscriptionService _subscriptionService;
        private readonly SessionService _sessionService;
        private readonly ILogger _logger;
        private readonly IMemoryCache _cache;
        private readonly AsyncRetryPolicy _retryPolicy;

        private const string FeesCacheKeyPrefix = "stripe_fee_";
        private const string InvoiceCacheKeyPrefix = "stripe_invoice_";
        private const string SubscriptionCacheKeyPrefix = "stripe_subscription_";
        public string Name => this.Name;

        /// <summary>
        /// Initializes a new instance of the <see cref="StripeService"/> class.
        /// </summary>
        /// <param name="stripeSettings">The Stripe API configuration settings.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="cache">Optional memory cache for performance optimization.</param>
        public StripeService(
            IOptions<StripeSettings> stripeSettings,
            IMemoryCache cache = null
            )
        {
            if (stripeSettings == null || string.IsNullOrEmpty(stripeSettings.Value.ApiSecret))
            {
                throw new ArgumentNullException(nameof(stripeSettings), "Stripe API secret is required.");
            }

            // Configure Stripe API with the secret key
            StripeConfiguration.ApiKey = stripeSettings.Value.ApiSecret;

            _chargeService = new ChargeService();
            _balanceTransactionService = new BalanceTransactionService();
            _paymentIntentService = new PaymentIntentService();
            _invoiceService = new InvoiceService();
            _subscriptionService = new SubscriptionService();
            _sessionService = new SessionService();
            _logger = new Logger<StripeService>(new LoggerFactory());
            _cache = cache; // Cache is optional

            // Configure retry policy for resilient API calls
            _retryPolicy = Policy
                .Handle<StripeException>(ex => IsRetryableStripeException(ex))
                .Or<HttpRequestException>()
                .WaitAndRetryAsync(
                    3, // Number of retry attempts
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), // Exponential backoff
                    (exception, timeSpan, retryCount, context) =>
                    {
                        _logger.LogWarning(
                            exception,
                            "Retry {RetryCount} after {RetryDelay}ms for operation {OperationKey}",
                            retryCount,
                            timeSpan.TotalMilliseconds,
                            context.OperationKey);
                    });
        }

        /// <summary>
        /// Determines if a Stripe exception is retryable based on its HTTP status code.
        /// </summary>
        /// <param name="ex">The Stripe exception to check.</param>
        /// <returns>True if the exception is retryable; otherwise, false.</returns>
        private bool IsRetryableStripeException(StripeException ex)
        {
            // Retry on rate limits, server errors and network issues
            return ex.HttpStatusCode == System.Net.HttpStatusCode.TooManyRequests ||
                   ex.HttpStatusCode == System.Net.HttpStatusCode.ServiceUnavailable ||
                   ex.HttpStatusCode == System.Net.HttpStatusCode.GatewayTimeout ||
                   ex.HttpStatusCode == System.Net.HttpStatusCode.InternalServerError;
        }

        /// <summary>
        /// Retrieves a payment intent from Stripe by its ID.
        /// </summary>
        /// <param name="id">The ID of the payment intent to retrieve.</param>
        /// <param name="options">Optional payment intent retrieval options.</param>
        /// <returns>The payment intent.</returns>
        /// <exception cref="PaymentApiException">Thrown when an error occurs during payment processing.</exception>
        public async Task<PaymentIntent> GetPaymentIntentAsync(string id, PaymentIntentGetOptions options = null)
        {
            using var activity = new Activity("StripeService.GetPaymentIntent").Start();
            activity.SetTag("PaymentIntentId", id);

            try
            {
                _logger.LogInformation("Retrieving payment intent {PaymentIntentId}", id);

                return await _retryPolicy.ExecuteAsync(async (context) =>
                {
                    var paymentIntent = await _paymentIntentService.GetAsync(id, options);

                    if (paymentIntent == null)
                    {
                        throw new PaymentApiException(
                            $"Failed to retrieve payment intent {id}", "Stripe", id);
                    }

                    return paymentIntent;
                }, new Context("GetPaymentIntent"));
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, "Stripe error retrieving payment intent {PaymentIntentId}: {Message}",
                    id, ex.Message);
                throw new PaymentApiException(
                    $"Error retrieving payment intent: {ex.Message}", "Stripe", id, ex);
            }
            catch (Exception ex) when (ex is not PaymentApiException)
            {
                _logger.LogError(ex, "Unexpected error retrieving payment intent {PaymentIntentId}", id);
                throw new PaymentApiException(
                    "An unexpected error occurred while processing the payment intent", "Stripe", id, ex);
            }
        }

        /// <summary>
        /// Retrieves the fee for a payment from Stripe.
        /// </summary>
        /// <param name="paymentIntentId">The ID of the payment intent.</param>
        /// <returns>The Stripe fee in decimal format.</returns>
        /// <exception cref="PaymentApiException">Thrown when an error occurs during fee retrieval.</exception>
        public async Task<decimal> GetFeeAsync(string paymentIntentId)
        {
            string cacheKey = $"{FeesCacheKeyPrefix}{paymentIntentId}";

            // Try to get from cache first if caching is available
            if (_cache != null && _cache.TryGetValue(cacheKey, out decimal cachedFee))
            {
                _logger.LogDebug("Retrieved Stripe fee from cache for payment intent {PaymentIntentId}: {Fee}",
                    paymentIntentId, cachedFee);
                return cachedFee;
            }

            using var activity = new Activity("StripeService.GetFee").Start();
            activity.SetTag("PaymentIntentId", paymentIntentId);

            int attempt = 0;
            const int MaxRetries = 5;
            const int RetryDelaySeconds = 2;

            while (attempt < MaxRetries)
            {
                try
                {
                    _logger.LogInformation("Retrieving fee for payment intent {PaymentIntentId}, attempt {Attempt}",
                        paymentIntentId, attempt + 1);

                    // Step 1: Fetch the PaymentIntent with the latest_charge expanded
                    var paymentIntentService = new PaymentIntentService();
                    var paymentIntent = await paymentIntentService.GetAsync(
                        paymentIntentId,
                        new PaymentIntentGetOptions { Expand = new List<string> { "latest_charge" } }
                    );

                    // Step 2: Check if the latest charge exists
                    if (string.IsNullOrEmpty(paymentIntent.LatestChargeId))
                    {
                        _logger.LogWarning(
                            "No charge found for PaymentIntent {PaymentIntentId} on attempt {Attempt}",
                            paymentIntentId, attempt + 1);

                        attempt++;
                        if (attempt >= MaxRetries)
                        {
                            throw new PaymentApiException(
                                "Maximum retry attempts exceeded for fee retrieval", "Stripe", paymentIntentId);
                        }

                        await Task.Delay(TimeSpan.FromSeconds(RetryDelaySeconds * attempt));
                        continue;
                    }

                    // Step 3: Fetch the Charge using the latest_charge ID
                    var charge = await _chargeService.GetAsync(
                        paymentIntent.LatestChargeId,
                        new ChargeGetOptions { Expand = new List<string> { "balance_transaction" } }
                    );

                    // Step 4: Verify the charge status
                    if (charge.Status != "succeeded")
                    {
                        _logger.LogWarning(
                            "Charge {ChargeId} for PaymentIntent {PaymentIntentId} is not succeeded. Status: {Status} on attempt {Attempt}",
                            charge.Id, paymentIntentId, charge.Status, attempt + 1);

                        attempt++;
                        if (attempt >= MaxRetries)
                        {
                            throw new PaymentApiException(
                                $"Charge is not in succeeded state. Current status: {charge.Status}",
                                "Stripe", paymentIntentId);
                        }

                        await Task.Delay(TimeSpan.FromSeconds(RetryDelaySeconds * attempt));
                        continue;
                    }

                    // Step 5: Retrieve the balance transaction
                    var balanceTransaction = charge.BalanceTransaction;
                    if (balanceTransaction == null)
                    {
                        _logger.LogWarning(
                            "Failed to retrieve BalanceTransaction for Charge {ChargeId} on attempt {Attempt}",
                            charge.Id, attempt + 1);

                        attempt++;
                        if (attempt >= MaxRetries)
                        {
                            throw new PaymentApiException(
                                "Unable to retrieve fee details", "Stripe", paymentIntentId);
                        }

                        await Task.Delay(TimeSpan.FromSeconds(RetryDelaySeconds * attempt));
                        continue;
                    }

                    // Step 6: Calculate the fee (converted from cents to decimal)
                    var chargeAmount = Math.Round(
                        (decimal)((balanceTransaction.Fee / balanceTransaction.ExchangeRate)),
                        2,
                        MidpointRounding.AwayFromZero);

                    decimal stripeFee = chargeAmount / 100m;

                    // Cache the result if caching is available
                    if (_cache != null)
                    {
                        var cacheOptions = new MemoryCacheEntryOptions()
                            .SetAbsoluteExpiration(TimeSpan.FromHours(24))
                            .SetPriority(CacheItemPriority.Normal);

                        _cache.Set(cacheKey, stripeFee, cacheOptions);
                    }

                    _logger.LogInformation("Successfully retrieved fee for {PaymentIntentId}: {Fee}",
                        paymentIntentId, stripeFee);

                    return stripeFee;
                }
                catch (StripeException ex)
                {
                    _logger.LogError(ex, "Stripe error retrieving fee for {PaymentIntentId}: {Message}",
                        paymentIntentId, ex.Message);

                    // Only retry on specific types of Stripe errors
                    if (!IsRetryableStripeException(ex) || attempt >= MaxRetries - 1)
                    {
                        throw new PaymentApiException(
                            $"Error retrieving fee: {ex.Message}", "Stripe", paymentIntentId, ex);
                    }

                    attempt++;
                    await Task.Delay(TimeSpan.FromSeconds(RetryDelaySeconds * attempt));
                }
                catch (Exception ex) when (ex is not PaymentApiException)
                {
                    _logger.LogError(ex, "Unexpected error retrieving fee for {PaymentIntentId}", paymentIntentId);

                    if (attempt >= MaxRetries - 1)
                    {
                        throw new PaymentApiException(
                            "An unexpected error occurred while retrieving fee information",
                            "Stripe", paymentIntentId, ex);
                    }

                    attempt++;
                    await Task.Delay(TimeSpan.FromSeconds(RetryDelaySeconds * attempt));
                }
            }

            // This should never be reached due to the exception in the last iteration
            throw new PaymentApiException(
                "Maximum retry attempts exceeded", "Stripe", paymentIntentId);
        }

        /// <summary>
        /// Retrieves an invoice from Stripe.
        /// </summary>
        /// <param name="invoiceId">The ID of the invoice to retrieve.</param>
        /// <returns>The invoice.</returns>
        /// <exception cref="PaymentApiException">Thrown when an error occurs during invoice retrieval.</exception>
        public async Task<Domain.DTOs.Payment.Invoice> GetInvoiceAsync(string invoiceId)
        {
            if (string.IsNullOrEmpty(invoiceId))
            {
                throw new ArgumentNullException(nameof(invoiceId), "Invoice ID is required");
            }

            string cacheKey = $"{InvoiceCacheKeyPrefix}{invoiceId}";

            // Try to get from cache first if caching is available
            if (_cache != null && _cache.TryGetValue(cacheKey, out Domain.DTOs.Payment.Invoice cachedInvoice))
            {
                return cachedInvoice;
            }

            using var activity = new Activity("StripeService.GetInvoice").Start();
            activity.SetTag("InvoiceId", invoiceId);

            try
            {
                _logger.LogInformation("Retrieving invoice {InvoiceId}", invoiceId);

                var invoice = await _retryPolicy.ExecuteAsync(async (context) =>
                {
                    var stripeInvoice = await _invoiceService.GetAsync(invoiceId);

                    if (stripeInvoice == null)
                    {
                        throw new PaymentApiException(
                            $"Failed to retrieve invoice {invoiceId}", "Stripe", invoiceId);
                    }

                    var result = new Domain.DTOs.Payment.Invoice()
                    {
                        Id = stripeInvoice.Id,
                        CreatedAt = stripeInvoice.Created,
                        AmountDue = stripeInvoice.AmountDue,
                        AmountPaid = stripeInvoice.AmountPaid,
                        AmountRemaining = stripeInvoice.AmountRemaining,
                        Currency = stripeInvoice.Currency,
                        CustomerEmail = stripeInvoice.CustomerEmail,
                        DueDate = stripeInvoice.DueDate,
                        Paid = stripeInvoice.Paid,
                        Tax = stripeInvoice.Tax,
                        Discount = stripeInvoice.TotalDiscountAmounts?.Sum(d => d.Amount) ?? 0,
                        Total = stripeInvoice.Total,
                        Status = stripeInvoice.Status,
                    };

                    // Cache the result if caching is available
                    if (_cache != null)
                    {
                        var cacheOptions = new MemoryCacheEntryOptions()
                            .SetAbsoluteExpiration(TimeSpan.FromHours(1))
                            .SetPriority(CacheItemPriority.Normal);

                        _cache.Set(cacheKey, result, cacheOptions);
                    }

                    return result;
                }, new Context("GetInvoice"));

                return invoice;
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, "Stripe error retrieving invoice {InvoiceId}: {Message}",
                    invoiceId, ex.Message);
                throw new PaymentApiException(
                    $"Error retrieving invoice: {ex.Message}", "Stripe", invoiceId, ex);
            }
            catch (Exception ex) when (ex is not PaymentApiException)
            {
                _logger.LogError(ex, "Unexpected error retrieving invoice {InvoiceId}", invoiceId);
                throw new PaymentApiException(
                    "An unexpected error occurred while retrieving the invoice",
                    "Stripe", invoiceId, ex);
            }
        }

        /// <summary>
        /// Retrieves the next due date for a subscription from Stripe.
        /// </summary>
        /// <param name="invoiceId">The ID of the invoice associated with the subscription.</param>
        /// <returns>The next due date, or null if not available.</returns>
        public async Task<DateTime?> GetNextDueDate(string invoiceId)
        {
            if (string.IsNullOrEmpty(invoiceId))
            {
                _logger.LogInformation("No invoice ID provided for next due date retrieval");
                return null;
            }

            try
            {
                _logger.LogInformation("Retrieving next due date for invoice {InvoiceId}", invoiceId);

                return await _retryPolicy.ExecuteAsync(async (context) =>
                {
                    var invoice = await _invoiceService.GetAsync(invoiceId);

                    if (invoice == null || string.IsNullOrEmpty(invoice.SubscriptionId))
                    {
                        _logger.LogInformation("No subscription associated with invoice {InvoiceId}", invoiceId);
                        return await Task.FromResult<DateTime?>(null);
                    }

                    var subscription = await _subscriptionService.GetAsync(invoice.SubscriptionId);

                    if (subscription == null || subscription.Status == "canceled")
                    {
                        _logger.LogInformation("Subscription {SubscriptionId} not found or canceled",
                            invoice.SubscriptionId);
                        return await Task.FromResult<DateTime?>(null);
                    }

                    // Wrap the DateTime? in a Task
                    return await Task.FromResult<DateTime?>(subscription.CurrentPeriodEnd);
                }, new Context("GetNextDueDate"));
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, "Stripe error retrieving next due date for invoice {InvoiceId}: {Message}",
                    invoiceId, ex.Message);
                // Return null instead of throwing to avoid breaking payment flow for this non-critical operation
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error retrieving next due date for invoice {InvoiceId}",
                    invoiceId);
                // Return null instead of throwing to avoid breaking payment flow for this non-critical operation
                return null;
            }
        }

        /// <summary>
        /// Retrieves a subscription from Stripe.
        /// </summary>
        /// <param name="subscriptionId">The ID of the subscription to retrieve.</param>
        /// <returns>The subscription.</returns>
        public async Task<Domain.DTOs.Payment.Subscription> GetSubscriptionAsync(string subscriptionId)
        {
            if (string.IsNullOrEmpty(subscriptionId))
            {
                _logger.LogInformation("No subscription ID provided");
                return new Domain.DTOs.Payment.Subscription()
                {
                    NextDueDate = DateTime.UtcNow
                };
            }

            string cacheKey = $"{SubscriptionCacheKeyPrefix}{subscriptionId}";

            // Try to get from cache first if caching is available
            if (_cache != null && _cache.TryGetValue(cacheKey, out Domain.DTOs.Payment.Subscription cachedSubscription))
            {
                return cachedSubscription;
            }

            try
            {
                _logger.LogInformation("Retrieving subscription {SubscriptionId}", subscriptionId);

                return await _retryPolicy.ExecuteAsync(async (context) =>
                {
                    var subscription = await _subscriptionService.GetAsync(subscriptionId);

                    var result = new Domain.DTOs.Payment.Subscription()
                    {
                        // Use the subscription's CurrentPeriodEnd if available, otherwise use current time
                        NextDueDate = subscription != null ? subscription.CurrentPeriodEnd : DateTime.UtcNow
                    };

                    // Cache the result if caching is available
                    if (_cache != null && subscription != null)
                    {
                        var cacheOptions = new MemoryCacheEntryOptions()
                            .SetAbsoluteExpiration(TimeSpan.FromMinutes(30))
                            .SetPriority(CacheItemPriority.Normal);

                        _cache.Set(cacheKey, result, cacheOptions);
                    }

                    return result;
                }, new Context("GetSubscription"));
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, "Stripe error retrieving subscription {SubscriptionId}: {Message}",
                    subscriptionId, ex.Message);
                // Return default subscription instead of throwing to avoid breaking payment flow
                return new Domain.DTOs.Payment.Subscription()
                {
                    NextDueDate = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error retrieving subscription {SubscriptionId}",
                    subscriptionId);
                // Return default subscription instead of throwing to avoid breaking payment flow
                return new Domain.DTOs.Payment.Subscription()
                {
                    NextDueDate = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Retrieves a subscription by payment ID from Stripe.
        /// </summary>
        /// <param name="paymentProviderId">The ID of the payment.</param>
        /// <returns>The subscription.</returns>
        public async Task<Domain.DTOs.Payment.Subscription> GetSubscriptionByPaymentAsync(string paymentProviderId)
        {
            if (string.IsNullOrEmpty(paymentProviderId))
            {
                throw new ArgumentNullException(nameof(paymentProviderId), "Payment ID is required");
            }

            try
            {
                _logger.LogInformation("Retrieving subscription by payment {PaymentId}", paymentProviderId);

                return await _retryPolicy.ExecuteAsync(async (context) =>
                {
                    var payment = await _paymentIntentService.GetAsync(paymentProviderId);

                    if (payment?.Invoice == null)
                    {
                        _logger.LogWarning("No invoice associated with payment {PaymentId}", paymentProviderId);
                        return new Domain.DTOs.Payment.Subscription()
                        {
                            NextDueDate = DateTime.UtcNow
                        };
                    }

                    var invoice = await _invoiceService.GetAsync(payment.Invoice.Id);

                    if (invoice?.Subscription == null)
                    {
                        _logger.LogWarning("No subscription associated with invoice {InvoiceId} for payment {PaymentId}",
                            payment.Invoice.Id, paymentProviderId);
                        return new Domain.DTOs.Payment.Subscription()
                        {
                            NextDueDate = DateTime.UtcNow
                        };
                    }

                    var subscription = await _subscriptionService.GetAsync(invoice.Subscription.Id);

                    return new Domain.DTOs.Payment.Subscription()
                    {
                        NextDueDate = subscription != null ? subscription.CurrentPeriodEnd : DateTime.UtcNow
                    };
                }, new Context("GetSubscriptionByPayment"));
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, "Stripe error retrieving subscription by payment {PaymentId}: {Message}",
                    paymentProviderId, ex.Message);
                throw new PaymentApiException(
                    $"Error retrieving subscription by payment: {ex.Message}",
                    "Stripe", paymentProviderId, ex);
            }
            catch (Exception ex) when (ex is not PaymentApiException)
            {
                _logger.LogError(ex, "Unexpected error retrieving subscription by payment {PaymentId}",
                    paymentProviderId);
                throw new PaymentApiException(
                    "An unexpected error occurred while retrieving the subscription",
                    "Stripe", paymentProviderId, ex);
            }
        }

        public async Task<string> CreateCheckoutSession(Guid userId, Guid subscriptionId, decimal amount, string interval)
        {
            var options = new SessionCreateOptions
            {
                PaymentMethodTypes = new List<string> { "card" },
                LineItems = new List<SessionLineItemOptions>
            {
                new SessionLineItemOptions
                {
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        UnitAmount = (long)(amount * 100), // Convert to cents
                        Currency = "usd",
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = "Investment Subscription"
                        },
                        Recurring = interval != "one-time" ? new SessionLineItemPriceDataRecurringOptions
                        {
                            Interval = interval
                        } : null
                    },
                    Quantity = 1
                }
            },
                Mode = interval == "one-time" ? "payment" : "subscription",
                SuccessUrl = "https://localhost:5173/success?session_id={CHECKOUT_SESSION_ID}",
                CancelUrl = "https://localhost:5173/cancel",
                Metadata = new Dictionary<string, string> { { "subscriptionId", subscriptionId.ToString() } }
            };

            var session = await _sessionService.CreateAsync(options);
            return session.Url;
        }
    }
}