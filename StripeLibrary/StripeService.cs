using Application.Interfaces;
using Domain.Constants;
using Domain.DTOs;
using Domain.DTOs.Payment;
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
        private readonly IOptions<StripeSettings> _settings;
        private readonly ChargeService _chargeService;
        private readonly PaymentIntentService _paymentIntentService;
        private readonly InvoiceService _invoiceService;
        private readonly SubscriptionService _subscriptionService;
        private readonly SessionService _sessionService;
        private readonly CustomerService _customerService;
        private readonly ILogger _logger;
        private readonly IMemoryCache _cache;
        private readonly AsyncRetryPolicy _retryPolicy;

        private const string FeesCacheKeyPrefix = "stripe_fee_";
        private const string InvoiceCacheKeyPrefix = "stripe_invoice_";
        private const string SubscriptionCacheKeyPrefix = "stripe_subscription_";
        public string Name => "Stripe";

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

            _settings = stripeSettings;
            _chargeService = new ChargeService();
            _paymentIntentService = new PaymentIntentService();
            _invoiceService = new InvoiceService();
            _subscriptionService = new SubscriptionService();
            _sessionService = new SessionService();
            _customerService = new CustomerService();
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
        /// Searches for customers in Stripe based on search options
        /// </summary>
        /// <param name="searchOptions">Dictionary of search options (e.g. query, limit)</param>
        /// <returns>A list of matching Stripe customers</returns>
        public async Task<IEnumerable<Stripe.Customer>> SearchCustomersAsync(Dictionary<string, object> searchOptions)
        {
            try
            {
                // Validate input
                if (searchOptions == null)
                {
                    throw new ArgumentNullException(nameof(searchOptions), "Search options cannot be null");
                }

                // Create search options
                var options = new Stripe.CustomerSearchOptions();

                // Apply search query if provided
                if (searchOptions.TryGetValue("query", out var query) && query != null)
                {
                    options.Query = query.ToString();
                }

                // Apply limit if provided
                if (searchOptions.TryGetValue("limit", out var limit) && limit != null && int.TryParse(limit.ToString(), out int limitValue))
                {
                    options.Limit = limitValue;
                }
                else
                {
                    // Default limit to avoid retrieving too many records
                    options.Limit = 10;
                }

                // Optional: Apply additional parameters
                if (searchOptions.TryGetValue("expand", out var expand) && expand is List<string> expandList)
                {
                    options.Expand = expandList;
                }

                // Execute the search
                var searchResult = await _customerService.SearchAsync(options);

                // Return the data (or empty list if null)
                return searchResult?.Data ?? new List<Stripe.Customer>();
            }
            catch (Stripe.StripeException ex)
            {
                _logger.LogError(ex, "Stripe error searching for customers: {Message}, {Type}", ex.Message, ex.StripeError?.Type);
                throw new PaymentApiException($"Failed to search customers in Stripe: {ex.Message}", "Stripe");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching for customers in Stripe: {Message}", ex.Message);
                throw new PaymentApiException("An unexpected error occurred while searching for customers", "Stripe");
            }
        }

        /// <summary>
        /// Creates a new customer in Stripe
        /// </summary>
        /// <param name="customerOptions">Dictionary of customer creation options</param>
        /// <returns>The created Stripe customer</returns>
        public async Task<Customer> CreateCustomerAsync(Dictionary<string, object> customerOptions)
        {
            try
            {
                // Validate input
                if (customerOptions == null)
                {
                    throw new ArgumentNullException(nameof(customerOptions), "Customer options cannot be null");
                }

                // Create customer creation options
                var options = new CustomerCreateOptions();

                // Set email if provided
                if (customerOptions.TryGetValue("email", out var email) && email != null)
                {
                    options.Email = email.ToString();
                }

                // Set name if provided
                if (customerOptions.TryGetValue("name", out var name) && name != null)
                {
                    options.Name = name.ToString();
                }

                // Set description if provided
                if (customerOptions.TryGetValue("description", out var description) && description != null)
                {
                    options.Description = description.ToString();
                }

                // Set phone if provided
                if (customerOptions.TryGetValue("phone", out var phone) && phone != null)
                {
                    options.Phone = phone.ToString();
                }

                // Set metadata if provided
                if (customerOptions.TryGetValue("metadata", out var metadataObj) && metadataObj is Dictionary<string, string> metadata)
                {
                    options.Metadata = new Dictionary<string, string>(metadata);
                }

                // Set payment method if provided
                if (customerOptions.TryGetValue("payment_method", out var paymentMethod) && paymentMethod != null)
                {
                    options.PaymentMethod = paymentMethod.ToString();
                }

                // Optionally set default invoice settings
                if (customerOptions.TryGetValue("invoice_settings", out var invoiceSettingsObj) &&
                    invoiceSettingsObj is Dictionary<string, object> invoiceSettings)
                {
                    options.InvoiceSettings = new CustomerInvoiceSettingsOptions();

                    if (invoiceSettings.TryGetValue("default_payment_method", out var defaultPaymentMethod) &&
                        defaultPaymentMethod != null)
                    {
                        options.InvoiceSettings.DefaultPaymentMethod = defaultPaymentMethod.ToString();
                    }
                }

                // Execute the customer creation
                _logger.LogInformation("Creating new Stripe customer with email: {Email}", options.Email);
                var customer = await _customerService.CreateAsync(options);

                _logger.LogInformation("Successfully created Stripe customer: {CustomerId}", customer.Id);
                return customer;
            }
            catch (Stripe.StripeException ex)
            {
                _logger.LogError(ex, "Stripe error creating customer: {Message}, {Type}",
                    ex.Message, ex.StripeError?.Type);
                throw new PaymentApiException($"Failed to create customer in Stripe: {ex.Message}", "Stripe");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating customer in Stripe: {Message}", ex.Message);
                throw new PaymentApiException("An unexpected error occurred while creating a customer", "Stripe");
            }
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
        /// Retrieves an invoice from Stripe by its ID.
        /// </summary>
        /// <param name="id">The ID of the payment intent to retrieve.</param>
        /// <returns>The invoice.</returns>
        /// <exception cref="PaymentApiException">Thrown when an error occurs during invoice retrieval.</exception>
        public async Task<Invoice> GetInvoiceAsync(string id)
        {
            using var activity = new Activity("StripeService.GetInvoiceAsync").Start();
            activity.SetTag("InvoiceId", id);

            try
            {
                _logger.LogInformation("Retrieving invoice {InvoiceId}", id);

                return await _retryPolicy.ExecuteAsync(async (context) =>
                {
                    var invoice = await _invoiceService.GetAsync(id);

                    if (invoice == null)
                    {
                        throw new PaymentApiException(
                            $"Failed to retrieve invoice {id}", "Stripe", id);
                    }

                    return invoice;
                }, new Context("GetInvoice"));
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, "Stripe error retrieving invoice {InvoiceId}: {Message}",
                    id, ex.Message);
                throw new PaymentApiException(
                    $"Error retrieving incoice: {ex.Message}", "Stripe", id, ex);
            }
            catch (Exception ex) when (ex is not PaymentApiException)
            {
                _logger.LogError(ex, "Unexpected error retrieving invoice {InvoiceId}", id);
                throw new PaymentApiException(
                    "An unexpected error occurred while processing invoice.paid event", "Stripe", id, ex);
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
                    var paymentIntent = await _paymentIntentService.GetAsync(
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
                        (decimal)(balanceTransaction.Fee / balanceTransaction.ExchangeRate!),
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
            throw new PaymentApiException("Maximum retry attempts exceeded", "Stripe", paymentIntentId);
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
        /// Retrieves a subscription by payment ID from Stripe.
        /// </summary>
        /// <param name="paymentProviderId">The ID of the payment.</param>
        /// <returns>The subscription.</returns>
        public async Task<PaymentSubscriptionDto> GetSubscriptionByPaymentAsync(string paymentProviderId)
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
                        return new Domain.DTOs.Payment.PaymentSubscriptionDto()
                        {
                            NextDueDate = DateTime.UtcNow
                        };
                    }

                    var invoice = await _invoiceService.GetAsync(payment.Invoice.Id);

                    if (invoice?.Subscription == null)
                    {
                        _logger.LogWarning("No subscription associated with invoice {InvoiceId} for payment {PaymentId}",
                            payment.Invoice.Id, paymentProviderId);
                        return new Domain.DTOs.Payment.PaymentSubscriptionDto()
                        {
                            NextDueDate = DateTime.UtcNow
                        };
                    }

                    var subscription = await _subscriptionService.GetAsync(invoice.Subscription.Id);

                    return new Domain.DTOs.Payment.PaymentSubscriptionDto()
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

        public async Task<ResultWrapper<SessionDto>> CreateCheckoutSession(Guid userId, Guid subscriptionId, decimal amount, string interval)
        {
            try
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

                return ResultWrapper<SessionDto>.Success(new SessionDto
                {
                    ClientSecret = session.ClientSecret,
                    Url = session.Url,
                    Status = session.Status
                });
            }
            catch (Exception ex)
            {
                return ResultWrapper<SessionDto>.FromException(ex);
            }

        }

        public async Task<ResultWrapper<SessionDto>> CreateCheckoutSessionWithOptions(CheckoutSessionOptions options)
        {
            try
            {
                // Convert line items to Stripe format
                var stripeLineItems = new List<SessionLineItemOptions>();
                foreach (var item in options.LineItems)
                {
                    // Create price data with proper recurring configuration for subscription mode
                    var priceDataOptions = new SessionLineItemPriceDataOptions
                    {
                        Currency = item.Currency,
                        UnitAmount = item.UnitAmount,
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = item.Name,
                            Description = item.Description
                        }
                    };

                    // Add recurring configuration if in subscription mode
                    if (options.Mode == "subscription")
                    {
                        // Set up the recurring component
                        priceDataOptions.Recurring = new SessionLineItemPriceDataRecurringOptions
                        {
                            // Convert your interval to Stripe's format
                            // Stripe only accepts: day, week, month, or year
                            Interval = ConvertIntervalToStripeFormat(item.Interval),

                            // You can also set interval count if needed
                            // IntervalCount = 1 
                        };
                    }

                    stripeLineItems.Add(new SessionLineItemOptions
                    {
                        PriceData = priceDataOptions,
                        Quantity = item.Quantity
                    });
                }

                var sessionOptions = new SessionCreateOptions
                {
                    PaymentMethodTypes = new List<string> { options.PaymentMethodType },
                    LineItems = stripeLineItems,
                    Mode = options.Mode,
                    SuccessUrl = options.SuccessUrl,
                    CancelUrl = options.CancelUrl,
                    Metadata = options.Metadata
                };

                if (sessionOptions.Mode == "subscription")
                {
                    // If this is a subscription checkout, make sure the metadata transfers to the subscription
                    sessionOptions.SubscriptionData = new SessionSubscriptionDataOptions
                    {
                        Metadata = sessionOptions.Metadata // This is the key part - it copies the metadata to the subscription
                    };
                }

                if (!string.IsNullOrEmpty(options.CustomerId))
                {
                    sessionOptions.Customer = options.CustomerId;
                }

                var session = await _sessionService.CreateAsync(sessionOptions);

                return ResultWrapper<SessionDto>.Success(new SessionDto
                {
                    Provider = Name,
                    Id = session.Id,
                    Url = session.Url,
                    ClientSecret = session.ClientSecret,
                    SubscriptionId = session.SubscriptionId,
                    InvoiceId = session.InvoiceId,
                    Metadata = options.Metadata,
                    Status = session.Status
                });
            }
            catch (Exception ex)
            {
                return ResultWrapper<SessionDto>.FromException(ex);
            }
        }

        public async Task<ResultWrapper> CancelPaymentAsync(string paymentId, string? reason = "requested_by_customer")
        {
            try
            {
                var cancelOptions = new Stripe.PaymentIntentCancelOptions
                {
                    CancellationReason = "requested_by_customer"
                };

                var cancelledIntent = await _paymentIntentService.CancelAsync(paymentId, cancelOptions);

                return ResultWrapper.Success("Payment cacelled successfully.");
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, "Stripe error cancelling payment {PaymentId}: {Message}", paymentId, ex.Message);
                return ResultWrapper.Failure(FailureReason.PaymentProcessingError, $"Failed to cancel payment: {ex.Message}");
            }
        }

        // Add to StripeLibrary/StripeService.cs
        public async Task<ResultWrapper> RetryPaymentAsync(string paymentIntentId, string subscriptionId)
        {
            try
            {
                // Get Stripe subscription
                var subscription = await GetSubscriptionAsync(subscriptionId);
                if (subscription == null)
                    throw new KeyNotFoundException($"Stripe subscription {subscriptionId} not found");

                // Get default payment method
                var paymentMethodId = subscription.DefaultPaymentMethod.Id;
                if (string.IsNullOrEmpty(paymentMethodId))
                    throw new PaymentApiException("No default payment method found for subscription", "Stripe");

                // Create a new payment intent
                var options = new PaymentIntentCreateOptions
                {
                    Amount = subscription.Items.Data[0].Price.UnitAmount ?? 0,
                    Currency = subscription.Items.Data[0].Price.Currency,
                    Customer = subscription.CustomerId,
                    PaymentMethod = paymentMethodId,
                    Confirm = true,
                    OffSession = true,
                    Metadata = new Dictionary<string, string>
                    {
                        ["subscriptionId"] = subscription.Metadata.TryGetValue("subscriptionId", out var subId) ? subId : string.Empty,
                        ["userId"] = subscription.Metadata.TryGetValue("userId", out var userId) ? userId : string.Empty
                    }
                };

                var intent = await _paymentIntentService.CreateAsync(options);

                return ResultWrapper.Success();
            }
            catch (StripeException ex)
            {
                // Handle specific Stripe errors
                string errorMessage = ex.StripeError?.Message ?? "Stripe payment retry failed";
                string errorCode = ex.StripeError?.Code ?? "unknown";

                return ResultWrapper.Failure(FailureReason.ThirdPartyServiceUnavailable, $"{errorMessage} (Code: {errorCode})");
            }
            catch (Exception ex)
            {
                return ResultWrapper.FromException(ex);
            }
        }

        public async Task<ResultWrapper<SessionDto>> CreateUpdatePaymentMethodSessionAsync(
            string subscriptionId,
            Dictionary<string, string> metadata)
        {
            try
            {
                // Get Stripe subscription
                var subscription = await GetSubscriptionAsync(subscriptionId);
                if (subscription == null)
                    throw new KeyNotFoundException($"Stripe subscription {subscriptionId} not found");

                // Create session for updating the payment method
                var options = new SessionCreateOptions
                {
                    Mode = "setup",
                    Customer = subscription.CustomerId,
                    SetupIntentData = new SessionSetupIntentDataOptions
                    {
                        Metadata = metadata
                    },
                    SuccessUrl = _settings.Value.PaymentUpdateSuccessUrl + "?session_id={CHECKOUT_SESSION_ID}",
                    CancelUrl = _settings.Value.PaymentUpdateCancelUrl,
                    PaymentMethodTypes = new List<string> { "card" },
                    Metadata = metadata
                };

                var service = new Stripe.Checkout.SessionService();
                var session = await service.CreateAsync(options);

                return ResultWrapper<SessionDto>.Success(new SessionDto
                {
                    Id = session.Id,
                    Provider = "Stripe",
                    ClientSecret = session.ClientSecret,
                    Url = session.Url,
                    SubscriptionId = subscriptionId,
                    Metadata = metadata,
                    Status = session.Status
                });
            }
            catch (StripeException ex)
            {
                string errorMessage = ex.StripeError?.Message ?? "Stripe error creating update payment session";
                return ResultWrapper<SessionDto>.Failure(FailureReason.ThirdPartyServiceUnavailable, errorMessage);
            }
            catch (Exception ex)
            {
                return ResultWrapper<SessionDto>.FromException(ex);
            }
        }

        /// <summary>
        /// Retrieves a subscription from Stripe.
        /// </summary>
        /// <param name="subscriptionId">The ID of the subscription to retrieve.</param>
        /// <returns>The subscription.</returns>
        private async Task<Subscription?> GetSubscriptionAsync(string subscriptionId)
        {
            if (string.IsNullOrEmpty(subscriptionId))
            {
                throw new ArgumentNullException("No subscription ID provided");
            }

            string cacheKey = $"{SubscriptionCacheKeyPrefix}{subscriptionId}";

            // Try to get from cache first if caching is available
            if (_cache != null && _cache.TryGetValue(cacheKey, out Subscription cachedSubscription))
            {
                return cachedSubscription!;
            }

            try
            {
                _logger.LogInformation("Retrieving subscription {SubscriptionId}", subscriptionId);

                return await _retryPolicy.ExecuteAsync(async (context) =>
                {
                    var subscription = await _subscriptionService.GetAsync(subscriptionId);

                    // Cache the result if caching is available
                    if (_cache != null && subscription != null)
                    {
                        var cacheOptions = new MemoryCacheEntryOptions()
                            .SetAbsoluteExpiration(TimeSpan.FromMinutes(30))
                            .SetPriority(CacheItemPriority.Normal);

                        _cache.Set(cacheKey, subscription, cacheOptions);
                    }

                    return subscription;
                }, new Context("GetSubscription"));
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, "Stripe error retrieving subscription {SubscriptionId}: {Message}",
                    subscriptionId, ex.Message);
                // Return default subscription instead of throwing to avoid breaking payment flow
                return default;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error retrieving subscription {SubscriptionId}",
                    subscriptionId);
                // Return default subscription instead of throwing to avoid breaking payment flow
                return default;
            }
        }

        // Helper method to convert your interval format to Stripe's format
        private string ConvertIntervalToStripeFormat(string interval)
        {
            // Map your interval constants to Stripe's expected values
            return interval switch
            {
                "DAILY" => "day",
                "WEEKLY" => "week",
                "MONTHLY" => "month",
                "YEARLY" => "year",
                _ => "month" // Default to month
            };
        }

    }
}