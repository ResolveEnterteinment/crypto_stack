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
        private readonly IAssetService _assetService;
        private readonly IPaymentService _paymentService;
        private readonly ILogger<PaymentWebhookController> _logger;

        public PaymentWebhookController(
            IPaymentService paymentService,
            IAssetService assetService,
            ILogger<PaymentWebhookController> logger)
        {
            _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
            _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [HttpPost]
        [Route("stripe")]
        public async Task<IActionResult> HandleWebhook([FromBody] Event stripeEvent)
        {
            #region Validate
            if (stripeEvent == null)
            {
                _logger.LogWarning("Received null Stripe event");
                return BadRequest("Stripe event is required.");
            }

            if (stripeEvent.Type != "payment_intent.succeeded")
            {
                _logger.LogInformation("Received non-success event: {EventType}", stripeEvent.Type);
                return Ok();
            }

            var paymentIntent = stripeEvent.Data.Object as PaymentIntent;
            if (paymentIntent == null)
            {
                _logger.LogWarning("Failed to cast event data to PaymentIntent");
                return BadRequest("Invalid event data: Expected PaymentIntent.");
            }
            #endregion Validate

            try
            {
                var totalAmount = paymentIntent.Amount / 100m;
                var paymentFee = (totalAmount * 0.029m) + 0.30m;
                var platformFee = totalAmount * 0.01m;
                var netAmount = totalAmount - paymentFee - platformFee;

                var paymentRequest = new PaymentRequest
                {
                    UserId = paymentIntent.Metadata["userId"],
                    SubscriptionId = paymentIntent.Metadata["subscriptionId"],
                    PaymentId = paymentIntent.Id,
                    TotalAmount = totalAmount,
                    PaymentProviderFee = paymentFee,
                    PlatformFee = platformFee,
                    NetAmount = netAmount,
                    Currency = paymentIntent.Currency,
                    Status = paymentIntent.Status
                };

                // Validate required fields
                if (string.IsNullOrWhiteSpace(paymentRequest.UserId) || string.IsNullOrWhiteSpace(paymentRequest.SubscriptionId))
                {
                    _logger.LogWarning("Missing metadata in PaymentIntent {PaymentId}: UserId or SubscriptionId", paymentIntent.Id);
                    return BadRequest("Missing required metadata: userId or subscriptionId.");
                }

                var paymentResult = await _paymentService.ProcessPaymentRequest(paymentRequest);
                if (!paymentResult.IsSuccess)
                {
                    _logger.LogError("Failed to process payment request for PaymentId {PaymentId}: {Error}", paymentRequest.PaymentId, paymentResult.ErrorMessage);
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