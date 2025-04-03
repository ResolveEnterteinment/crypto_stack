using Application.Contracts.Requests.Payment;
using Application.Interfaces;
using Application.Interfaces.Payment;
using Domain.Constants;
using Domain.DTOs.Event;
using Domain.DTOs.Settings;
using Domain.Events;
using Domain.Exceptions;
using Domain.Models.Subscription;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Polly;
using Stripe;
using System.Diagnostics;
using System.Text;

namespace crypto_investment_project.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [EnableRateLimiting("webhook")]  // Apply rate limiting to prevent DoS
    [IgnoreAntiforgeryToken] // Add this attribute to bypass token validation for webhooks
    public class PaymentWebhookController : ControllerBase
    {
        private readonly IPaymentService _paymentService;
        private readonly ISubscriptionService _subscriptionService;
        private readonly IEventService _eventService;
        private readonly IIdempotencyService _idempotencyService;
        private readonly ILogger<PaymentWebhookController> _logger;
        private readonly string _webhookSecret;

        public PaymentWebhookController(
            IPaymentService paymentService,
            ISubscriptionService subscriptionService,
            IEventService eventService,
            IIdempotencyService idempotencyService,
            IOptions<StripeSettings> stripeSettings,
            ILogger<PaymentWebhookController> logger)
        {
            _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
            _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
            _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
            _idempotencyService = idempotencyService ?? throw new ArgumentNullException(nameof(idempotencyService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _webhookSecret = stripeSettings.Value.WebhookSecret ?? throw new ArgumentNullException(nameof(stripeSettings.Value.WebhookSecret));
        }

        /// <summary>
        /// Handles incoming Stripe webhook events, verifying their signature and processing them appropriately.
        /// </summary>
        /// <returns>An IActionResult indicating the processing outcome.</returns>
        [HttpPost]
        [Route("stripe")]
        public async Task<IActionResult> StripeWebhook()
        {
            var correlationId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["CorrelationId"] = correlationId,
                ["Operation"] = "StripeWebhook"
            }))
            {
                try
                {
                    // Read the request body for signature verification
                    string requestBody;
                    using (var reader = new StreamReader(HttpContext.Request.Body, Encoding.UTF8))
                    {
                        requestBody = await reader.ReadToEndAsync();
                    }

                    // Verify webhook signature
                    string signature = Request.Headers["Stripe-Signature"];
                    if (string.IsNullOrEmpty(signature))
                    {
                        _logger.LogWarning("Missing Stripe-Signature header");
                        return BadRequest(new { error = "Missing signature header" });
                    }

                    // Verify the webhook signature
                    var stripeEvent = EventUtility.ConstructEvent(
                        requestBody,
                        signature,
                        _webhookSecret
                    );

                    _logger.LogInformation("Received Stripe webhook {EventId} of type {EventType}",
                        stripeEvent.Id, stripeEvent.Type);

                    // Check for idempotency - use Stripe event ID as the idempotency key
                    string idempotencyKey = $"stripe-webhook-{stripeEvent.Id}";
                    if (await _idempotencyService.HasKeyAsync(idempotencyKey))
                    {
                        _logger.LogInformation("Duplicate Stripe event {EventId} of type {EventType} received and skipped",
                            stripeEvent.Id, stripeEvent.Type);
                        return Ok(new { status = "Already processed" });
                    }

                    IActionResult result = Empty;

                    // Handle different event types
                    switch (stripeEvent.Type)
                    {
                        case "charge.updated":
                            result = await HandleChargeUpdated(stripeEvent, correlationId);
                            break;
                        case "customer.subscription.created":
                            result = await HandleSubscriptionCreated(stripeEvent, correlationId);
                            break;
                        /*case "checkout.session.completed":
                            result = await HandleCheckoutSessionCompletedAsync(stripeEvent);
                            break;*/
                        case "customer.subscription.updated":
                            result = await HandleSubscriptionUpdatedAsync(stripeEvent);
                            break;
                        case "customer.subscription.deleted":
                            result = await HandleSubscriptionDeletedAsync(stripeEvent);
                            break;
                        default:
                            result = HandleUnprocessedEventType(stripeEvent.Type);
                            break;
                    }

                    // Mark the event as processed for idempotency
                    await _idempotencyService.StoreResultAsync(idempotencyKey, new { status = "processed", timestamp = DateTime.UtcNow });

                    return result;
                }
                catch (StripeException ex)
                {
                    _logger.LogError(ex, "Stripe exception occurred: {Message}", ex.Message);

                    // Return 400 for invalid signatures or malformed requests to prevent retries
                    if (ex.StripeError?.Type == "invalid_request_error")
                    {
                        return BadRequest(new { error = ex.Message });
                    }

                    // Return 500 for other Stripe errors to allow retries
                    return StatusCode(500, new { error = "An error occurred processing the webhook" });
                }
                catch (ValidationException ex)
                {
                    _logger.LogWarning(ex, "Validation error processing webhook");
                    return BadRequest(new { error = ex.Message, validationErrors = ex.ValidationErrors });
                }
                catch (Exception ex)
                {
                    // Log unexpected errors but return 200 to prevent Stripe from retrying events
                    // that will always fail due to bugs in our code
                    _logger.LogError(ex, "Unexpected error processing Stripe webhook");
                    return Ok(new { status = "Error occurred but was logged", message = "Contact support with correlation ID" });
                }
            }
        }

        private async Task<IActionResult> HandleChargeUpdated(Event stripeEvent, string correlationId)
        {
            try
            {
                _logger.LogInformation("Processing charge.updated event {EventId}", stripeEvent.Id);

                // Cast event data to Charge object
                var charge = stripeEvent.Data.Object as Charge;
                if (charge == null)
                {
                    throw new ValidationException("Invalid event data", new Dictionary<string, string[]>
                    {
                        ["EventData"] = new[] { "Expected Charge object but received something else." }
                    });
                }

                // Early exit if charge is not succeeded
                if (charge.Status != "succeeded")
                {
                    _logger.LogInformation("Charge {ChargeId} is not succeeded. Status: {Status}", charge.Id, charge.Status);
                    return Ok(new { status = "Skipped", reason = "Charge not in succeeded state" });
                }

                // Safety check for required fields
                if (string.IsNullOrEmpty(charge.PaymentIntentId))
                {
                    throw new ValidationException("Missing payment intent", new Dictionary<string, string[]>
                    {
                        ["PaymentIntentId"] = new[] { "The charge must have an associated payment intent." }
                    });
                }

                ChargeRequest chargeRequest = new()
                {
                    Id = charge.Id,
                    PaymentIntentId = charge.PaymentIntentId,
                    Amount = charge.Amount,
                    Currency = charge.Currency
                };

                // Process the charge using the payment service
                var paymentResult = await _paymentService.ProcessChargeUpdatedEventAsync(chargeRequest);

                if (!paymentResult.IsSuccess)
                {
                    // Add correlation ID to the error message for traceability
                    throw new PaymentApiException(
                        $"Failed to process charge.updated event: {paymentResult.ErrorMessage}. Reference: {correlationId}",
                        "Stripe",
                        charge.Id);
                }

                _logger.LogInformation("Successfully processed charge.updated event for Charge {ChargeId}, PaymentId: {PaymentId}",
                    charge.Id, paymentResult.Data);

                return Ok(new { status = "Success", paymentId = paymentResult.Data });
            }
            catch (Exception ex) when (!(ex is ValidationException || ex is PaymentApiException))
            {
                // Wrap in PaymentProcessingException for better error handling
                throw new PaymentApiException(
                    $"Failed to process charge updated event: {ex.Message}. Reference: {correlationId}",
                    "Stripe",
                    stripeEvent.Id);
            }
        }

        private async Task<IActionResult> HandleSubscriptionCreated(Event stripeEvent, string correlationId)
        {
            try
            {
                _logger.LogInformation("Processing customer.subscription.created event {EventId}", stripeEvent.Id);

                // Cast event data to Subscription object
                var subscription = stripeEvent.Data.Object as Subscription;

                if (subscription == null)
                {
                    throw new ValidationException("Invalid event data", new Dictionary<string, string[]>
                    {
                        ["EventData"] = new[] { "Expected Subscription object but received something else." }
                    });
                }

                // Retrieve local subscription id
                if (!subscription.Metadata.TryGetValue("subscriptionId", out var subscriptionId) ||
                    string.IsNullOrEmpty(subscriptionId))
                {
                    throw new ValidationException("Invalid metadata", new Dictionary<string, string[]>
                    {
                        ["Metadata"] = new[] { "The subscription metadata must contain a valid subscriptionId." }
                    });
                }

                // Early exit if subscription is not active
                if (subscription.Status != "active")
                {
                    _logger.LogInformation("Subscription {SubscriptionId} is not active. Status: {Status}",
                        subscription.Id, subscription.Status);
                    return Ok(new { status = "Skipped", reason = "Subscription not active" });
                }

                var paymentProviderEvent = new PaymentProviderEvent()
                {
                    Provider = "Stripe",
                    Type = stripeEvent.Type,
                    Object = stripeEvent.Object,
                    Data = stripeEvent.Data.Object,
                };

                var storedEvent = new Domain.Models.Event.EventData
                {
                    EventType = typeof(SubscriptionCreatedEvent).Name,
                    Payload = subscriptionId
                };

                // Apply retry pattern for storing event data
                var retryPolicy = Polly.Policy
                    .Handle<Exception>()
                    .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                        (ex, time, retryCount, ctx) =>
                        {
                            _logger.LogWarning(ex, "Attempt {RetryCount} to store subscription event failed. Retrying in {RetryTime}ms",
                                retryCount, time.TotalMilliseconds);
                        });

                var storedEventResult = await retryPolicy.ExecuteAsync(async () =>
                    await _eventService.InsertOneAsync(storedEvent));

                if (!storedEventResult.IsAcknowledged || storedEventResult.InsertedId is null)
                {
                    throw new DatabaseException($"Failed to store subscription created event: {storedEventResult.ErrorMessage}");
                }

                await _eventService.Publish(new SubscriptionCreatedEvent(paymentProviderEvent, storedEventResult.InsertedId.Value));

                _logger.LogInformation("Published subscription created event for subscription id: {SubscriptionId}", subscriptionId);
                return Ok(new { status = "Success", subscriptionId = subscriptionId });
            }
            catch (Exception ex) when (!(ex is ValidationException || ex is DatabaseException))
            {
                // Wrap in PaymentProcessingException for better error handling
                throw new PaymentApiException(
                    $"Failed to process subscription created event: {ex.Message}. Reference: {correlationId}",
                    "Stripe",
                    stripeEvent.Id);
            }
        }

        private async Task<IActionResult> HandleInvoicePaid(Event stripeEvent, string correlationId)
        {
            try
            {
                _logger.LogInformation("Processing invoice.paid event {EventId}", stripeEvent.Id);

                // Cast event data to Invoice object
                var invoice = stripeEvent.Data.Object as Stripe.Invoice;
                if (invoice == null)
                {
                    throw new ValidationException("Invalid event data", new Dictionary<string, string[]>
                    {
                        ["EventData"] = new[] { "Expected Invoice object but received something else." }
                    });
                }

                // Handle logic for paid invoices
                // This is a placeholder for additional invoice processing logic
                _logger.LogInformation("Invoice {InvoiceId} paid for customer {CustomerId}",
                    invoice.Id, invoice.CustomerId);

                return Ok(new { status = "Success", invoiceId = invoice.Id });
            }
            catch (Exception ex) when (!(ex is ValidationException))
            {
                throw new PaymentApiException(
                    $"Failed to process invoice paid event: {ex.Message}. Reference: {correlationId}",
                    "Stripe",
                    stripeEvent.Id);
            }
        }
        private async Task<IActionResult> HandleCheckoutSessionCompletedAsync(Event stripeEvent)
        {
            var session = stripeEvent.Data.Object as Stripe.Checkout.Session;

            #region Validate

            if (session == null)
            {
                return BadRequest(new { message = "Invalid event data: Expected Checkout.Session object" });
            }

            // Extract subscription ID from metadata
            if (session.Metadata == null ||
                !session.Metadata.TryGetValue("subscriptionId", out var subscriptionId) ||
                string.IsNullOrEmpty(subscriptionId) ||
                !Guid.TryParse(subscriptionId, out var parsedSubscriptionId))
            {
                _logger.LogWarning("Missing or invalid subscriptionId in checkout session metadata");
                return BadRequest(new { message = "Invalid metadata: Missing subscriptionId" });
            }

            #endregion

            // Update subscription status to active
            var updateResult = await _subscriptionService.UpdateSubscriptionStatusAsync(parsedSubscriptionId, SubscriptionStatus.Active);

            if (updateResult == null || !updateResult.IsAcknowledged)
            {
                //TODO: retry with poly back-off or integrate poly back-off retry logic to base service
            }

            // If this was a one-time payment (not a subscription), we need to create a payment record
            if (session.Mode == "payment" && !string.IsNullOrEmpty(session.PaymentIntentId))
            {
                // Extract user ID from metadata
                if (!session.Metadata.TryGetValue("userId", out var userId) || string.IsNullOrEmpty(userId))
                {
                    _logger.LogWarning("Missing userId in checkout session metadata");
                    return BadRequest(new { message = "Invalid metadata: Missing userId" });
                }

                // Create payment request
                var paymentRequest = new PaymentIntentRequest
                {
                    UserId = userId,
                    SubscriptionId = subscriptionId,
                    Provider = "Stripe",
                    PaymentId = session.PaymentIntentId,
                    InvoiceId = session.InvoiceId,
                    TotalAmount = session.AmountTotal.GetValueOrDefault() / 100m, // Convert from cents
                    PaymentProviderFee = 0, // Will be updated when processing the charge
                    PlatformFee = session.AmountTotal.GetValueOrDefault() / 100m * 0.01m, // 1% platform fee
                    NetAmount = session.AmountTotal.GetValueOrDefault() / 100m * 0.99m, // Net after platform fee
                    Currency = session.Currency?.ToUpperInvariant() ?? "USD",
                    Status = "succeeded"
                };

                await _paymentService.ProcessPaymentIntentSucceededEvent(paymentRequest);
            }

            return Ok("Checkout session completed successfully");
        }

        private async Task<IActionResult> HandleSubscriptionUpdatedAsync(Event stripeEvent)
        {
            var stripeSubscription = stripeEvent.Data.Object as Stripe.Subscription;
            if (stripeSubscription == null)
            {
                throw new ArgumentException("Invalid event data: Expected Subscription object");
            }

            // Find our subscription with this Stripe subscription ID
            var filter = Builders<SubscriptionData>.Filter.Eq(s => s.ProviderSubscriptionId, stripeSubscription.Id);
            var subscriptionData = await _subscriptionService.GetOneAsync(filter);

            if (subscriptionData == null)
            {
                _logger.LogWarning("No subscription found with provider subscription ID: {ProviderSubscriptionId}", stripeSubscription.Id);
                return BadRequest(new { error = "No matching subscription found" });
            }

            // Update based on subscription status
            var updatedFields = new Dictionary<string, object>
            {
                ["NextDueDate"] = stripeSubscription.CurrentPeriodEnd
            };

            switch (stripeSubscription.Status)
            {
                case "active":
                    updatedFields["Status"] = SubscriptionStatus.Active;
                    break;
                case "canceled":
                case "unpaid":
                case "past_due":
                    updatedFields["Status"] = SubscriptionStatus.Cancelled;
                    updatedFields["IsCancelled"] = true;
                    break;
                case "trialing":
                case "incomplete":
                case "incomplete_expired":
                    updatedFields["Status"] = SubscriptionStatus.Pending;
                    break;
            }

            await _subscriptionService.UpdateOneAsync(subscriptionData.Id, updatedFields);

            return Ok("Subscription updated successfully");
        }

        private async Task<IActionResult> HandleSubscriptionDeletedAsync(Event stripeEvent)
        {
            var subscription = stripeEvent.Data.Object as Stripe.Subscription;
            if (subscription == null)
            {
                throw new ArgumentException("Invalid event data: Expected Subscription object");
            }

            // Find our subscription with this Stripe subscription ID
            var filter = Builders<Domain.Models.Subscription.SubscriptionData>.Filter.Eq(s => s.ProviderSubscriptionId, subscription.Id);
            var subscriptionData = await _subscriptionService.GetOneAsync(filter);

            if (subscriptionData == null)
            {
                _logger.LogWarning("No subscription found with provider subscription ID: {ProviderSubscriptionId}", subscription.Id);
                return BadRequest(new { error = "No matching subscription found" });
            }

            // Mark subscription as cancelled
            var updatedFields = new Dictionary<string, object>
            {
                ["Status"] = SubscriptionStatus.Cancelled,
                ["IsCancelled"] = true
            };

            await _subscriptionService.UpdateOneAsync(subscriptionData.Id, updatedFields);

            return Ok("Subscription cancelled successfully");
        }

        private IActionResult HandleUnprocessedEventType(string eventType)
        {
            _logger.LogInformation("Received unhandled Stripe event type: {EventType}", eventType);
            return Ok(new { status = "Acknowledged", message = $"Event type {eventType} does not require processing" });
        }
    }
}