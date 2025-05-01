using Application.Interfaces;
using Application.Interfaces.Logging;
using Application.Interfaces.Payment;
using Domain.DTOs.Settings;
using Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Stripe;
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
        private readonly ILoggingService _logger;
        private readonly string _webhookSecret;

        public PaymentWebhookController(
            IPaymentWebhookHandler webhookHandler,
            IIdempotencyService idempotencyService,
            IOptions<StripeSettings> stripeSettings,
            ILoggingService logger)
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
            using var scope = _logger.BeginScope();

            try
            {
                // Trace incoming webhook
                await _logger.LogTraceAsync("Received Stripe webhook request");

                // Read the request body
                string requestBody;
                using (var reader = new StreamReader(HttpContext.Request.Body, Encoding.UTF8))
                {
                    requestBody = await reader.ReadToEndAsync();
                }

                // Verify webhook signature
                string signature = Request.Headers["Stripe-Signature"];

                if (string.IsNullOrEmpty(signature))
                {
                    await _logger.LogTraceAsync("Missing Stripe-Signature header", level: Domain.Constants.Logging.LogLevel.Error, requiresResolution: true);
                    return BadRequest(new { error = "Missing signature header" });
                }

                var stripeEvent = EventUtility.ConstructEvent(
                    requestBody,
                    signature,
                    _webhookSecret,
                    300 // 5 minutes clock skew tolerance
                );

                using var EventScope = _logger.EnrichScope(
                    ("EventId", stripeEvent.Id),
                    ("EventType", stripeEvent.Type)
                );

                _logger.LogInformation($"Stripe event received: {stripeEvent.Type} (ID: {stripeEvent.Id})");

                // Idempotency check
                var stripeEventId = stripeEvent.Id;
                var idempotencyKey = $"stripe-webhook-{stripeEventId}";

                if (await _idempotencyService.HasKeyAsync(idempotencyKey))
                {
                    await _logger.LogTraceAsync($"Duplicate Stripe event detected: {stripeEventId}", level: Domain.Constants.Logging.LogLevel.Warning);
                    throw new IdempotencyException(idempotencyKey);
                }

                // Handle the webhook event
                var result = await _webhookHandler.HandleStripeEventAsync(stripeEvent);

                if (result == null || !result.IsSuccess)
                {
                    _logger.LogError($"Failed to handle Stripe event {stripeEvent.Id} of type {stripeEvent.Type}: {result?.ErrorMessage ?? "Unknown error"}");

                    return StatusCode(StatusCodes.Status500InternalServerError, new
                    {
                        error = result?.ErrorMessage ?? "Failed to process event",
                        eventId = stripeEvent.Id,
                        reason = result?.Reason.ToString() ?? "Unknown"
                    });
                }

                await _idempotencyService.StoreResultAsync(idempotencyKey, new { status = "processed", timestamp = DateTime.UtcNow });

                _logger.LogInformation($"Stripe event {stripeEvent.Id} processed successfully");

                return Ok(new
                {
                    status = "Processed",
                    message = result.DataMessage ?? "Event processed successfully",
                    eventId = stripeEvent.Id
                });
            }
            catch (StripeException ex)
            {
                _logger.LogError($"Stripe exception occurred: {ex.Message}");

                if (ex.StripeError?.Type == "invalid_request_error")
                {
                    return BadRequest(new { error = ex.Message });
                }

                return StatusCode(500, new { error = "An error occurred processing the webhook" });
            }
            catch (ValidationException ex)
            {
                _logger.LogWarning($"Validation error: {ex.Message}");

                return BadRequest(new { error = ex.Message, validationErrors = ex.ValidationErrors });
            }
            catch (Exception ex)
            {
                await _logger.LogTraceAsync($"Unexpected error processing webhook: {ex.Message}", level: Domain.Constants.Logging.LogLevel.Error);

                return Ok(); // Return OK to Stripe even if internal error happened to avoid retries
            }
        }

    }
}