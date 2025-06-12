using Application.Interfaces;
using Application.Interfaces.Asset;
using Application.Interfaces.Exchange;
using Domain.Constants.Asset;
using Domain.DTOs.Balance;
using Domain.Exceptions;
using Domain.Models.Balance;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using System.Security.Claims;

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
            var balancesResult = await _balanceService.FetchBalancesWithAssetsAsync(userId, AssetType.Exchange);
            return balancesResult == null || !balancesResult.IsSuccess || balancesResult.Data == null
                ? BadRequest(balancesResult?.ErrorMessage ?? "Balance result returned null.")
                : Ok(balancesResult.Data.Select(b => new BalanceDto(b)));
        }

        [HttpGet]
        [Route("asset/{ticker}")]
        public async Task<IActionResult> GetAssetBalance(string ticker)
        {
            var user = GetUserId();
            if (user is null || user == Guid.Empty)
            {
                return ValidationProblem("A valid user is required.");
            }
            var filter = new FilterDefinitionBuilder<BalanceData>().Where(b => b.UserId == user && b.Ticker == ticker);
            var balancesResult = await _balanceService.GetUserBalanceByTickerAsync(user.Value, ticker);
            return balancesResult == null || !balancesResult.IsSuccess || balancesResult.Data == null
                ? BadRequest(balancesResult?.ErrorMessage ?? "Balance result returned null.")
                : Ok(balancesResult.Data);
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
            return balancesResult == null || !balancesResult.IsSuccess || balancesResult.Data == null
                ? BadRequest(balancesResult?.ErrorMessage ?? "Balance result returned null.")
                : Ok(balancesResult.Data);
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
                {
                    throw new AssetFetchException($"Failed to fetch asset {balance.AssetId}");
                }

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

        private Guid? GetUserId()
        {
            string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out Guid parsedUserId)
                ? null
                : parsedUserId;
        }
    }
}
