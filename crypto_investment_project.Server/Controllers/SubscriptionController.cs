using Application.Contracts.Requests.Subscription;
using Domain.Services;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;

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
        public async Task<IActionResult> NewSubscription([FromBody] SubscriptionRequest subscriptionRequest)
        {
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

            if (string.IsNullOrWhiteSpace(subscriptionRequest.Interval))
            {
                return BadRequest("Interval is required.");
            }

            if (subscriptionRequest.Amount <= 0)
            {
                return BadRequest("Amount must be greater than zero.");
            }

            var subscriptionResult = await _subscriptionService.ProcessSubscriptionRequest(subscriptionRequest);

            if (subscriptionResult.IsSuccess)
            {
                return Ok($"Subscription #{subscriptionResult.Data} successfully created.");
            }
            else
            {
                return BadRequest(subscriptionResult.ErrorMessage);
            }
        }
    }
}