using Application.Interfaces;
using Application.Interfaces.Payment;
using Domain.DTOs.Settings;
using Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
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
        private readonly IPaymentWebhookHandler _webhookHandler;
        private readonly IIdempotencyService _idempotencyService;
        private readonly ILogger<PaymentWebhookController> _logger;
        private readonly string _webhookSecret;

        public PaymentWebhookController(
            IPaymentWebhookHandler webhookHandler,
            IIdempotencyService idempotencyService,
            IOptions<StripeSettings> stripeSettings,
            ILogger<PaymentWebhookController> logger)
        {
            _webhookHandler = webhookHandler ?? throw new ArgumentNullException(nameof(webhookHandler));
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
                        _webhookSecret,
                        300 // Tolerate 5 minute clock skew
                    );

                    _logger.LogInformation("Received Stripe webhook {EventId} of type {EventType}",
                        stripeEvent.Id, stripeEvent.Type);

                    // Check for idempotency - use Stripe event ID as the idempotency key
                    string stripeEventId = stripeEvent.Id;
                    string idempotencyKey = $"stripe-webhook-{stripeEventId}";

                    if (await _idempotencyService.HasKeyAsync(idempotencyKey))
                    {
                        _logger.LogInformation("Duplicate Stripe event {EventId}", stripeEventId);
                        throw new IdempotencyException(idempotencyKey);
                    }

                    // Process the event using our handler
                    var result = await _webhookHandler.HandleStripeEventAsync(stripeEvent, correlationId);

                    if (result == null || !result.IsSuccess)
                    {
                        _logger.LogWarning("Failed to handle Stripe event {EventId} of type {EventType}: {Message}",
                            stripeEvent.Id, stripeEvent.Type, result?.ErrorMessage ?? "Webhook handler returned null.");

                        return StatusCode(StatusCodes.Status500InternalServerError,
                            new
                            {
                                error = result.ErrorMessage ?? "Failed to process event",
                                eventId = stripeEvent.Id,
                                reason = result.Reason.ToString() ?? "Unknown"
                            });
                    }

                    await _idempotencyService.StoreResultAsync(idempotencyKey, new { status = "processed", timestamp = DateTime.UtcNow });

                    return Ok(new
                    {
                        status = "Processed",
                        message = result.DataMessage ?? "Event processed successfully",
                        eventId = stripeEvent.Id
                    });

                    // Mark the event as processed for idempotency
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
    }
}