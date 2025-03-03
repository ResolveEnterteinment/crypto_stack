using Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;

namespace crypto_investment_project.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BalanceController(IBalanceService balanceService) : ControllerBase
    {
        private readonly IBalanceService _balanceService = balanceService;

        [HttpPost]
        [Route("user/{user}")]
        public async Task<IActionResult> GetUserBalances(string user)
        {
            var isUserValid = ObjectId.TryParse(user, out var userId);
            if (user is null || user == string.Empty || !isUserValid)
            {
                return ValidationProblem("A valid user is required.");
            }
            var balances = await _balanceService.GetAllByUserIdAsync(userId);
            if (balances.IsSuccess)
            {
                var query = balances?.Data?
                .GroupBy(b => b.AssetId, (id, bal) => new
                {
                    CoinId = id.ToString(),
                    Available = bal.Select(b => b.Available).Sum(),
                    Locked = bal.Select(b => b.Locked).Sum(),
                });
                return Ok(query);
            }
            else
            {
                return BadRequest(balances.ErrorMessage);
            }
        }

        [HttpPost]
        [Route("subscription/{subscription}")]
        public async Task<IActionResult> GetSubscriptionBalances(string subscription)
        {
            var isSubscriptionValid = ObjectId.TryParse(subscription, out var subscriptionId);
            if (subscription is null || subscription == string.Empty || !isSubscriptionValid)
            {
                return ValidationProblem("A valid user is required.");
            }
            var balances = await _balanceService.GetAllBySubscriptionIdAsync(subscriptionId);
            if (balances.IsSuccess)
            {
                var query = balances?.Data?
                .GroupBy(b => b.AssetId, (id, bal) => new
                {
                    CoinId = id.ToString(),
                    Available = bal.Select(b => b.Available).Sum(),
                    Locked = bal.Select(b => b.Locked).Sum(),
                });
                return Ok(query);
            }
            else
            {
                return BadRequest(balances.ErrorMessage);
            }
        }
    }
}
