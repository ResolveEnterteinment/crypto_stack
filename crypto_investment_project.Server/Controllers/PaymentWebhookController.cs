using Application.Contracts.Requests.Payment;
using Application.Interfaces;
using Domain.Services;
using Microsoft.AspNetCore.Mvc;
using Stripe;

namespace Domain.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PaymentWebhookController : ControllerBase
    {
        private readonly IPaymentService _paymentService;
        private readonly ILogger<PaymentWebhookController> _logger;

        public PaymentWebhookController(
            IPaymentService paymentService,
            IAssetService assetService, // Retained for potential future use
            ILogger<PaymentWebhookController> logger)
        {
            _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Handles incoming Stripe webhook events, specifically charge.updated, delegating processing to PaymentService.
        /// </summary>
        /// <param name="stripeEvent">The Stripe event received from the webhook.</param>
        /// <returns>An IActionResult indicating the processing outcome (OK, BadRequest, or InternalServerError).</returns>
        [HttpPost]
        [Route("stripe")]
        public async Task<IActionResult> HandleWebhook([FromBody] Event stripeEvent)
        {
            #region Validate Event
            // Ensure the event is not null
            if (stripeEvent == null)
            {
                _logger.LogWarning("Received null Stripe event");
                return BadRequest("Stripe event is required.");
            }

            // Filter for charge.updated events only
            if (stripeEvent.Type != "charge.updated")
            {
                _logger.LogInformation("Received non-charge.updated event: {EventType}", stripeEvent.Type);
                return Ok();
            }

            // Cast event data to Charge object
            var charge = stripeEvent.Data.Object as Charge;
            if (charge == null)
            {
                _logger.LogWarning("Failed to cast event data to Charge");
                return BadRequest("Invalid event data: Expected Charge.");
            }

            // Early exit if charge is not succeeded
            if (charge.Status != "succeeded")
            {
                _logger.LogInformation("Charge {ChargeId} is not succeeded. Status: {Status}", charge.Id, charge.Status);
                return Ok();
            }
            #endregion Validate Event

            try
            {
                ChargeRequest chargeRequest = new()
                {
                    Id = charge.Id,
                    PaymentIntentId = charge.PaymentIntentId,
                    Amount = charge.Amount,
                    Currency = charge.Currency
                };
                // Delegate payment processing to PaymentService
                var paymentResult = await _paymentService.ProcessChargeUpdatedEventAsync(chargeRequest);

                if (!paymentResult.IsSuccess)
                {
                    _logger.LogError("Failed to process charge.updated event for Charge {ChargeId}: {Error}", charge.Id, paymentResult.ErrorMessage);
                    throw new Exception($"Failed to handle payment webhook: {paymentResult.ErrorMessage}");
                }

                _logger.LogInformation("Published payment received event for PaymentId: {PaymentId}", paymentResult.Data);
                return Ok($"Published payment received event for PaymentId: {paymentResult.Data}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to handle Stripe webhook for event {EventId}", stripeEvent.Id);
                return StatusCode(500); // Triggers Stripe retry
            }
        }
    }
}