using Application.Extensions;
using Application.Interfaces;
using Application.Interfaces.Exchange;
using Domain.Constants;
using Domain.Constants.Asset;
using Domain.DTOs;
using Domain.DTOs.Balance;
using Domain.Models.Balance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using System.Security.Claims;

namespace crypto_investment_project.Server.Controllers
{
    

    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // Ensure the user is authenticated for all actions
    public class BalanceController(
        IBalanceService balanceService,
        IExchangeService exchangeService,
        IUserService userService,
        ILogger<BalanceController> logger
        ) : ControllerBase
    {
        private readonly IBalanceService _balanceService = balanceService ?? throw new ArgumentNullException(nameof(balanceService));
        private readonly IExchangeService _exchangeService = exchangeService ?? throw new ArgumentNullException(nameof(exchangeService));
        private readonly IUserService _userService = userService ?? throw new ArgumentNullException(nameof(userService));
        private readonly ILogger<BalanceController> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        [HttpGet]
        [Route("get/all")]
        [Authorize]
        public async Task<IActionResult> GetUserBalances()
        {
            var userId = GetUserId();

            if (userId is null || userId == Guid.Empty)
            {
                return ResultWrapper.Failure(FailureReason.ValidationError,
                    "A valid user is required.")
                    .ToActionResult(this);
            }

            try
            {
                var balancesResult = await _balanceService.FetchBalancesWithAssetsAsync((Guid)userId, AssetType.Exchange);

                if (balancesResult == null || !balancesResult.IsSuccess || balancesResult.Data == null)
                {
                    return ResultWrapper.NotFound("Balance")
                        .ToActionResult(this);
                }

                return ResultWrapper.Success(balancesResult.Data.Select(b => new BalanceDto(b)))
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving balances for user ID {userId}");

                return ResultWrapper.InternalServerError()
                .ToActionResult(this);
            }
        }

        [HttpGet]
        [Route("get/asset/{ticker}")]
        public async Task<IActionResult> GetAssetBalance(string ticker)
        {
            var userId = GetUserId();

            if (userId is null || userId == Guid.Empty)
            {
                return ResultWrapper.Failure(FailureReason.ValidationError,
                    "A valid user is required.",
                    "INVALID_REQUEST")
                    .ToActionResult(this);
            }

            try
            {
                var filter = new FilterDefinitionBuilder<BalanceData>().Where(b => b.UserId == userId && b.Ticker == ticker);
                
                var balancesResult = await _balanceService.GetUserBalanceByTickerAsync(userId.Value, ticker);
                
                if (balancesResult == null || !balancesResult.IsSuccess || balancesResult.Data == null)
                {
                    return ResultWrapper.NotFound("Balance")
                        .ToActionResult(this);
                }
                
                return ResultWrapper.Success(new BalanceDto(balancesResult.Data))
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving user ID {userId} balances for asset {ticker}");

                return ResultWrapper.InternalServerError()
                .ToActionResult(this);
            }
        }

        [HttpGet]
        [Authorize(Roles = "ADMIN")]
        [Route("admin/user/{user}/asset/{ticker}")]
        public async Task<IActionResult> GetAssetBalanceForUser(string user, string ticker)
        {
            // Validate input
            if (string.IsNullOrEmpty(user) || !Guid.TryParse(user, out Guid userId) || userId == Guid.Empty)
            {
                return ResultWrapper.Failure(FailureReason.ValidationError,
                    "A valid user is required.",
                    "INVALID_REQUEST")
                    .ToActionResult(this);
            }

            // Verify user exists
            var userExists = await _userService.CheckUserExists(userId);
            if (!userExists)
            {
                return ResultWrapper.NotFound("User", userId.ToString())
                    .ToActionResult(this);
            }

            try
            {
                var filter = new FilterDefinitionBuilder<BalanceData>().Where(b => b.UserId == userId && b.Ticker == ticker);
                
                var balancesResult = await _balanceService.GetUserBalanceByTickerAsync(userId, ticker);
                
                if (balancesResult == null || !balancesResult.IsSuccess || balancesResult.Data == null)
                {
                    return ResultWrapper.NotFound("Balance")
                        .ToActionResult(this);
                }

                return ResultWrapper.Success(new BalanceDto(balancesResult.Data))
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving user ID {user} balances for asset {ticker}");

                return ResultWrapper.InternalServerError()
                .ToActionResult(this);
            }
        }

        [HttpGet]
        [Route("totalInvestments")]
        [Authorize]
        public async Task<IActionResult> GetTotalInvestments()
        {
            var userId = GetUserId();

            if (userId is null || userId == Guid.Empty)
            {
                return ResultWrapper.Failure(FailureReason.ValidationError,
                    "A valid user is required.",
                    "INVALID_REQUEST")
                    .ToActionResult(this);
            }

            try
            {
                var balancesResult = await _balanceService.GetAllByUserIdAsync(userId.Value, AssetType.Platform);
                if(balancesResult == null || !balancesResult.IsSuccess || balancesResult.Data == null)
                {
                    return ResultWrapper.NotFound("Balance")
                        .ToActionResult(this);
                }
                return ResultWrapper.Success(balancesResult.Data.Select(b => new BalanceDto(b)))
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving total investments for user ID {userId}");

                return ResultWrapper.InternalServerError()
                .ToActionResult(this);
            }
        }

        [HttpGet]
        [Route("portfolioValue")]
        public async Task<IActionResult> GetUserPortfolioValue(string user)
        {
            var userId = GetUserId();

            if (userId is null || userId == Guid.Empty)
            {
                return ResultWrapper.Failure(FailureReason.ValidationError,
                    "A valid user is required.",
                    "INVALID_REQUEST")
                    .ToActionResult(this);
            }

            try
            {
                var balancesResult = await _balanceService.FetchBalancesWithAssetsAsync(userId.Value, AssetType.Exchange);
                
                if (balancesResult == null || !balancesResult.IsSuccess || balancesResult.Data == null)
                {
                    return ResultWrapper.NotFound("Balance")
                        .ToActionResult(this);
                }
                
                var portfolioValue = 0m;
                
                var balances = balancesResult.Data;

                var tickers = balances.Select(b => b.Asset.Ticker).Distinct().ToList();

                var priceResults = await _exchangeService.GetCachedAssetPricesAsync(tickers);

                if(priceResults == null || !priceResults.IsSuccess || priceResults.Data == null)
                {
                    return ResultWrapper.Failure(FailureReason.ExchangeApiError,
                        $"Failed to retrieve asset prices: {priceResults.ErrorMessage}")
                        .ToActionResult(this);
                }

                var prices = priceResults.Data;

                foreach (var balance in balances)
                {
                    portfolioValue += balance.Total * (prices.TryGetValue(balance.Asset.Ticker, out var price) ? price : 1m);
                }

                return ResultWrapper.Success(portfolioValue)
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving total investments for user ID {userId}");

                return ResultWrapper.InternalServerError()
                .ToActionResult(this);
            }
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
