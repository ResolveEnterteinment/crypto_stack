using Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace WebApi.Controllers
{
    [ApiController]
    [Route("api/subscription-management")]
    [Authorize]
    public class SubscriptionManagementController : ControllerBase
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<SubscriptionManagementController> _logger;

        public SubscriptionManagementController(
            IUnitOfWork unitOfWork,
            ILogger<SubscriptionManagementController> logger)
        {
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Reactivates a suspended subscription
        /// </summary>
        /// <param name="subscriptionId">ID of the subscription to reactivate</param>
        /// <returns>Success or error result</returns>
        [HttpPost("reactivate/{subscriptionId}")]
        public async Task<IActionResult> ReactivateSubscription(string subscriptionId)
        {
            try
            {
                if (!Guid.TryParse(subscriptionId, out var parsedSubscriptionId))
                {
                    return BadRequest("Invalid subscription ID format");
                }

                // Get the current user ID from claims
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized("User ID not found in claims");
                }

                // Verify that the subscription belongs to the user
                var subscription = await _unitOfWork.Subscriptions.GetByIdAsync(parsedSubscriptionId);
                if (!subscription.IsSuccess || subscription.Data == null)
                {
                    return NotFound($"Subscription {subscriptionId} not found");
                }

                if (subscription.Data.UserId.ToString() != userId && !User.IsInRole("ADMIN"))
                {
                    return Forbid("You don't have permission to reactivate this subscription");
                }

                // Verify that the subscription is suspended
                if (subscription.Data.Status != Domain.Constants.SubscriptionStatus.Suspended)
                {
                    return BadRequest($"Can only reactivate suspended subscriptions. Current status: {subscription.Data.Status}");
                }

                // Reactivate the subscription
                var reactivateResult = await _unitOfWork.Subscriptions.ReactivateSubscriptionAsync(parsedSubscriptionId);
                if (!reactivateResult.IsSuccess)
                {
                    return BadRequest(reactivateResult.ErrorMessage);
                }

                return Ok(new { message = "Subscription successfully reactivated" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reactivating subscription {SubscriptionId}", subscriptionId);
                return StatusCode(500, "An error occurred while processing your request");
            }
        }
    }
}