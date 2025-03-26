using Application.Contracts.Requests.Payment;
using Application.Interfaces;
using Application.Interfaces.Payment;
using Domain.DTOs.Event;
using Domain.Events;
using Domain.Exceptions;
using Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Stripe;
using System.Diagnostics;

namespace crypto_investment_project.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PaymentWebhookController : ControllerBase
    {
        private readonly IPaymentService _paymentService;
        private readonly ISubscriptionService _subscriptionService;
        private readonly IEventService _eventService;
        private readonly IIdempotencyService _idempotencyService;
        private readonly ILogger<PaymentWebhookController> _logger;

        public PaymentWebhookController(
            IPaymentService paymentService,
            ISubscriptionService subscriptionService,
            IEventService eventService,
            IIdempotencyService idempotencyService,
            ILogger<PaymentWebhookController> logger)
        {
            _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
            _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
            _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
            _idempotencyService = idempotencyService ?? throw new ArgumentNullException(nameof(idempotencyService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Handles incoming Stripe webhook events, specifically charge.updated, delegating processing to PaymentService.
        /// </summary>
        /// <param name="stripeEvent">The Stripe event received from the webhook.</param>
        /// <returns>An IActionResult indicating the processing outcome (OK, BadRequest, or InternalServerError).</returns>
        [HttpPost]
        [Route("stripe")]
        public async Task<IActionResult> StripeWebhook([FromBody] Event stripeEvent)
        {
            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["EventId"] = stripeEvent?.Id,
                ["EventType"] = stripeEvent?.Type,
                ["Operation"] = "StripeWebhook",
                ["CorrelationId"] = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            }))
            {
                try
                {
                    // Ensure the event is not null
                    if (stripeEvent == null)
                    {
                        throw new ValidationException("Stripe event is required.", new Dictionary<string, string[]>
                        {
                            ["Event"] = new[] { "The request body must contain a valid Stripe event." }
                        });
                    }

                    // Check for idempotency - use Stripe event ID as the idempotency key
                    if (await _idempotencyService.HasKeyAsync(stripeEvent.Id))
                    {
                        _logger.LogInformation("Duplicate Stripe event {EventId} of type {EventType} received and skipped",
                            stripeEvent.Id, stripeEvent.Type);
                        return Ok("Event already processed");
                    }

                    IActionResult result;

                    // Handle different event types
                    switch (stripeEvent.Type)
                    {
                        case "charge.updated":
                            result = await HandleChargeUpdated(stripeEvent);
                            break;
                        case "customer.subscription.created":
                            result = await HandleSubscripitonCreated(stripeEvent);
                            break;
                        default:
                            result = HandleUnknown(stripeEvent.Type);
                            break;
                    }

                    // Mark the event as processed for idempotency
                    await _idempotencyService.StoreResultAsync(stripeEvent.Id, true);

                    return result;
                }
                catch (ValidationException ex)
                {
                    // Specific handling for validation errors
                    _logger.LogWarning("Validation error in Stripe webhook: {Message}", ex.Message);
                    throw;
                }
                catch (Exception ex)
                {
                    // Log the error but return 200 OK to Stripe to prevent retries for certain errors
                    if (ex is ResourceNotFoundException || ex is ConcurrencyException)
                    {
                        _logger.LogWarning(ex, "Non-fatal error processing Stripe webhook {EventId}: {Message}",
                            stripeEvent?.Id, ex.Message);
                        return Ok($"Event processed with warning: {ex.Message}");
                    }

                    // For all other errors, let the global exception handler take care of it
                    // This will return a 500 status to Stripe, which will trigger a retry
                    _logger.LogError(ex, "Error processing Stripe webhook {EventId}", stripeEvent?.Id);
                    throw;
                }
            }
        }

        private async Task<IActionResult> HandleSubscripitonCreated(Event stripeEvent)
        {
            try
            {
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
                    _logger.LogInformation("Subscription {SubscriptionId} is not active. Status: {Status}", subscription.Id, subscription.Status);
                    return Ok($"Subscription {subscription.Id} is not active. Status: {subscription.Status}");
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

                var storedEventResult = await _eventService.InsertOneAsync(storedEvent);
                if (!storedEventResult.IsAcknowledged || storedEventResult.InsertedId is null)
                {
                    throw new DatabaseException($"Failed to store subscription created event: {storedEventResult.ErrorMessage}");
                }

                await _eventService.Publish(new SubscriptionCreatedEvent(paymentProviderEvent, storedEventResult.InsertedId.Value));

                _logger.LogInformation("Published subscription created event for subscription id: {SubscriptionId}", subscriptionId);
                return Ok($"Published subscription created event for subscription id: {subscriptionId}");
            }
            catch (Exception ex) when (!(ex is ValidationException || ex is DatabaseException))
            {
                // Wrap in PaymentProcessingException for better error handling
                throw new PaymentProcessingException($"Failed to process subscription created event: {ex.Message}", "Stripe", stripeEvent?.Id);
            }
        }

        private async Task<IActionResult> HandleChargeUpdated(Event stripeEvent)
        {
            try
            {
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
                    return Ok($"Charge {charge.Id} is not succeeded. Status: {charge.Status}");
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
                    throw new PaymentProcessingException(
                        $"Failed to process charge.updated event: {paymentResult.ErrorMessage}",
                        "Stripe",
                        charge.Id);
                }

                _logger.LogInformation("Published payment received event for PaymentId: {PaymentId}", paymentResult.Data);
                return Ok($"Published payment received event for PaymentId: {paymentResult.Data}");
            }
            catch (Exception ex) when (!(ex is ValidationException || ex is PaymentProcessingException))
            {
                // Wrap in PaymentProcessingException for better error handling
                throw new PaymentProcessingException($"Failed to process charge updated event: {ex.Message}", "Stripe", stripeEvent?.Id);
            }
        }

        private IActionResult HandleUnknown(string eventType)
        {
            _logger.LogInformation("Received non-handled Stripe event type: {EventType}", eventType);
            return Ok($"Event type {eventType} is not handled by this endpoint");
        }
    }
}