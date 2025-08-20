using Application.Extensions;
using Domain.Constants;
using Domain.DTOs;
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
                    return ResultWrapper.ValidationError(new()
                    {
                        ["subscriptionId"] = ["Invalid subscription ID format"]
                    }).ToActionResult(this);
                }

                // Get the current user ID from claims
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                {
                    return ResultWrapper.Unauthorized()
                        .ToActionResult(this);
                }

                // Verify that the subscription belongs to the user
                var subscription = await _unitOfWork.Subscriptions.GetByIdAsync(parsedSubscriptionId);
                if (!subscription.IsSuccess || subscription.Data == null)
                {
                    return ResultWrapper.NotFound("Subscription", subscriptionId)
                        .ToActionResult(this);
                }

                if (subscription.Data.UserId.ToString() != userId && !User.IsInRole("ADMIN"))
                {
                    return ResultWrapper.Unauthorized()
                        .ToActionResult(this);
                }

                // Verify that the subscription is suspended
                if (subscription.Data.Status != SubscriptionStatus.Suspended)
                {
                    return ResultWrapper.Failure(
                        FailureReason.InvalidOperation, 
                        "Subscription can only be reactivated if it is suspended")
                        .ToActionResult(this);
                }

                // Reactivate the subscription
                var reactivateResult = await _unitOfWork.Subscriptions.ReactivateSubscriptionAsync(parsedSubscriptionId);
                if (!reactivateResult.IsSuccess)
                {
                    return ResultWrapper.Failure(
                        reactivateResult.Reason, 
                        "Failed to reactivate the subscription")
                        .ToActionResult(this);
                }

                return ResultWrapper.Success("Subscription reactivated successfully")
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reactivating subscription {SubscriptionId}", subscriptionId);

                return ResultWrapper.InternalServerError()
                        .ToActionResult(this);
            }
        }
    }
}