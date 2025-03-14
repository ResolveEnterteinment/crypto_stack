using Application.Contracts.Requests.Subscription;
using Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace crypto_investment_project.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SubscriptionController : ControllerBase
    {
        private readonly ISubscriptionService _subscriptionService;

        public SubscriptionController(ISubscriptionService subscriptionService)
        {
            _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
        }

        [HttpPost]
        [Route("new")]
        public async Task<IActionResult> New([FromBody] SubscriptionCreateRequest subscriptionRequest)
        {
            #region Validation
            if (subscriptionRequest == null)
            {
                return ValidationProblem("A valid subscription request is required.");
            }

            // Validate request
            if (string.IsNullOrWhiteSpace(subscriptionRequest.UserId) || !Guid.TryParse(subscriptionRequest.UserId, out _))
            {
                return BadRequest("UserId must be a valid Guid.");
            }

            if (subscriptionRequest.Allocations == null || !subscriptionRequest.Allocations.Any())
            {
                return BadRequest("Allocations must contain at least one item.");
            }

            var invalidAllocation = subscriptionRequest.Allocations.FirstOrDefault(a =>
                string.IsNullOrWhiteSpace(a.AssetId) || !Guid.TryParse(a.AssetId, out _) || a.PercentAmount > 100);
            if (invalidAllocation != null)
            {
                return BadRequest($"Invalid allocation: AssetId must be a valid Guid and PercentAmount must be 0-100. Found AssetId: {invalidAllocation.AssetId}, PercentAmount: {invalidAllocation.PercentAmount}");
            }

            var allocationSum = subscriptionRequest.Allocations.Select(a => (int)a.PercentAmount).Sum();
            if (allocationSum != 100)
            {
                return BadRequest("Invalid sum of asset allocations. Allocation percent amounts total must be 100.");
            }

            if (string.IsNullOrWhiteSpace(subscriptionRequest.Interval))
            {
                return BadRequest("Interval is required.");
            }

            if (subscriptionRequest.Amount <= 0)
            {
                return BadRequest("Amount must be greater than zero.");
            }
            #endregion

            var subscriptionResult = await _subscriptionService.ProcessSubscriptionCreateRequest(subscriptionRequest);

            if (subscriptionResult.IsSuccess)
            {
                return Ok($"Subscription #{subscriptionResult.Data} successfully created.");
            }
            else
            {
                return BadRequest(subscriptionResult.ErrorMessage);
            }
        }
        [HttpPost]
        [Route("update/{id}")]
        public async Task<IActionResult> Update([FromBody] SubscriptionUpdateRequest updateRequest, string id)
        {
            #region Validation
            if (!Guid.TryParse(id, out var subscriptionId))
            {
                return ValidationProblem("A valid subscription id is required.");
            }
            if (updateRequest == null)
            {
                return ValidationProblem("A valid update request is required.");
            }

            if (updateRequest.Allocations != null)
            {
                if (!updateRequest.Allocations.Any())
                {
                    return BadRequest("Allocations must contain at least one asset.");
                }
                var invalidAllocation = updateRequest.Allocations.FirstOrDefault(a =>
                    string.IsNullOrWhiteSpace(a.AssetId) || !Guid.TryParse(a.AssetId, out _) || a.PercentAmount > 100);
                if (invalidAllocation != null)
                {
                    return BadRequest($"Invalid allocation: AssetId must be a valid Guid and PercentAmount must be 0-100. Found AssetId: {invalidAllocation.AssetId}, PercentAmount: {invalidAllocation.PercentAmount}");
                }

                var allocationSum = updateRequest.Allocations.Select(a => (int)a.PercentAmount).Sum();
                if (allocationSum != 100)
                {
                    return BadRequest("Invalid sum of asset allocations. Allocation percent amounts total must be 100.");
                }
            }

            if (updateRequest.Amount != null && updateRequest.Amount <= 0)
            {
                return BadRequest("Amount must be greater than zero.");
            }
            #endregion
            var subscriptionUpdateResult = await _subscriptionService.ProcessSubscriptionUpdateRequest(subscriptionId, updateRequest);

            if (subscriptionUpdateResult.IsSuccess)
            {
                return Ok($"Subscription #{subscriptionUpdateResult.Data} updated successfully.");
            }
            else
            {
                return BadRequest(subscriptionUpdateResult.ErrorMessage);
            }
        }
    }
}