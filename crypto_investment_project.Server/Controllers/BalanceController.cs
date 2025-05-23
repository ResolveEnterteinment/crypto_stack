using Application.Interfaces;
using Application.Interfaces.Asset;
using Application.Interfaces.Exchange;
using Domain.Constants;
using Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace crypto_investment_project.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BalanceController(
        IBalanceService balanceService,
        IExchangeService exchangeService,
        IAssetService assetService
        ) : ControllerBase
    {
        private readonly IBalanceService _balanceService = balanceService;
        private readonly IExchangeService _exchangeService = exchangeService;
        private readonly IAssetService _assetService = assetService;

        [HttpGet]
        [Route("user/{user}")]
        public async Task<IActionResult> GetUserBalances(string user)
        {
            var isUserValid = Guid.TryParse(user, out var userId);
            if (user is null || user == string.Empty || !isUserValid)
            {
                return ValidationProblem("A valid user is required.");
            }
            var balancesResult = await _balanceService.GetAllByUserIdAsync(userId, AssetType.Exchange);
            if (balancesResult == null || !balancesResult.IsSuccess || balancesResult.Data == null)
            {
                return BadRequest(balancesResult?.ErrorMessage ?? "Balance result returned null.");
            }

            return Ok(balancesResult.Data);
        }

        [HttpGet]
        [Route("totalInvestments/{user}")]
        public async Task<IActionResult> GetTotalInvestments(string user)
        {
            var isUserValid = Guid.TryParse(user, out var userId);
            if (user is null || user == string.Empty || !isUserValid)
            {
                return ValidationProblem("A valid user is required.");
            }
            var balancesResult = await _balanceService.GetAllByUserIdAsync(userId, AssetType.Platform);
            if (balancesResult == null || !balancesResult.IsSuccess || balancesResult.Data == null)
            {
                return BadRequest(balancesResult?.ErrorMessage ?? "Balance result returned null.");
            }

            return Ok(balancesResult.Data);
        }

        [HttpGet]
        [Route("portfolioValue/{user}")]
        public async Task<IActionResult> GetUserPortfolioValue(string user)
        {
            var isUserValid = Guid.TryParse(user, out var userId);
            if (user is null || user == string.Empty || !isUserValid)
            {
                return ValidationProblem("A valid user is required.");
            }
            var balancesResult = await _balanceService.GetAllByUserIdAsync(userId, AssetType.Exchange);
            if (balancesResult == null || !balancesResult.IsSuccess || balancesResult.Data == null)
            {
                return BadRequest(balancesResult?.ErrorMessage ?? "Balance result returned null.");
            }
            var portfolioValue = 0m;
            foreach (var balance in balancesResult.Data)
            {
                var assetResult = await _assetService.GetByIdAsync(balance.AssetId);
                if (assetResult == null || !assetResult.IsSuccess)
                    throw new AssetFetchException($"Failed to fetch asset {balance.AssetId}");
                var asset = assetResult.Data;
                var priceResult = await _exchangeService.Exchanges[asset.Exchange].GetAssetPrice(asset.Ticker);
                if (priceResult is null || !priceResult.IsSuccess)
                {
                    continue;
                }
                portfolioValue += priceResult.Data * balance.Total;
            }

            return Ok(portfolioValue);
        }

        [HttpGet]
        [Route("subscription/{subscription}")]
        public async Task<IActionResult> GetSubscriptionBalances(string subscription)
        {
            /*var isSubscriptionValid = Guid.TryParse(subscription, out var subscriptionId);
            if (subscription is null || subscription == string.Empty || !isSubscriptionValid)
            {
                return ValidationProblem("A valid user is required.");
            }
            var balances = await _balanceService.GetAllByUserIdAsync(subscriptionId);
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
            }*/
            throw new NotImplementedException();
        }
    }
}
