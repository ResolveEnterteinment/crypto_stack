using Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace WebApi.Controllers
{
    [ApiController]
    [Route("api/payment-methods")]
    [Authorize]
    public class PaymentMethodController : ControllerBase
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<PaymentMethodController> _logger;

        public PaymentMethodController(
            IUnitOfWork unitOfWork,
            ILogger<PaymentMethodController> logger)
        {
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Creates a checkout session for updating a payment method
        /// </summary>
        /// <param name="subscriptionId">ID of the subscription to update payment method for</param>
        /// <returns>URL to redirect the user to for payment method update</returns>
        [HttpPost("update/{subscriptionId}")]
        public async Task<IActionResult> CreateUpdatePaymentMethodSession(string subscriptionId)
        {
            try
            {
                // Get the current user ID from claims
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized("User ID not found in claims");
                }

                // Verify that the subscription belongs to the user
                var subscription = await _unitOfWork.Subscriptions.GetByIdAsync(Guid.Parse(subscriptionId));
                if (!subscription.IsSuccess || subscription.Data == null)
                {
                    return NotFound($"Subscription {subscriptionId} not found");
                }

                if (subscription.Data.UserId.ToString() != userId && !User.IsInRole("ADMIN"))
                {
                    return Forbid("You don't have permission to update this subscription's payment method");
                }

                // Create the update payment method session
                var sessionResult = await _unitOfWork.Payments.CreateUpdatePaymentMethodSessionAsync(userId, subscriptionId);
                if (!sessionResult.IsSuccess)
                {
                    return BadRequest(sessionResult.ErrorMessage);
                }

                return Ok(new { url = sessionResult.Data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating update payment method session for subscription {SubscriptionId}", subscriptionId);
                return StatusCode(500, "An error occurred while processing your request");
            }
        }

        /// <summary>
        /// Manually retry a failed payment
        /// </summary>
        /// <param name="paymentId">ID of the failed payment to retry</param>
        /// <returns>Success or error result</returns>
        [HttpPost("retry/{paymentId}")]
        [Authorize(Roles = "ADMIN")]
        public async Task<IActionResult> RetryPayment(string paymentId)
        {
            try
            {
                if (!Guid.TryParse(paymentId, out var parsedPaymentId))
                {
                    return BadRequest("Invalid payment ID format");
                }

                var retryResult = await _unitOfWork.Payments.RetryPaymentAsync(parsedPaymentId);
                if (!retryResult.IsSuccess)
                {
                    return BadRequest(retryResult.ErrorMessage);
                }

                return Ok(new { message = "Payment retry initiated successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrying payment {PaymentId}", paymentId);
                return StatusCode(500, "An error occurred while processing your request");
            }
        }
    }
}