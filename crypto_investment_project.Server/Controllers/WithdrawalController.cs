// crypto_investment_project.Server/Controllers/WithdrawalController.cs
using Application.Contracts.Requests.Withdrawal;
using Application.Extensions;
using Application.Interfaces.Asset;
using Application.Interfaces.Withdrawal;
using Domain.Constants.Withdrawal;
using Domain.DTOs.Withdrawal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace crypto_investment_project.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    [EnableRateLimiting("standard")]
    public class WithdrawalController : ControllerBase
    {
        private readonly IWithdrawalService _withdrawalService;
        private readonly IAssetService _assetService;
        private readonly ILogger<WithdrawalController> _logger;

        public WithdrawalController(
            IWithdrawalService withdrawalService,
            IAssetService assetService,
            ILogger<WithdrawalController> logger)
        {
            _withdrawalService = withdrawalService ?? throw new ArgumentNullException(nameof(withdrawalService));
            _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [HttpGet("limits")]
        [Authorize]
        public async Task<IActionResult> GetWithdrawalLimits()
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var parsedUserId))
                {
                    return BadRequest(new { message = "Invalid user ID" });
                }

                var result = await _withdrawalService.GetUserWithdrawalLimitsAsync(parsedUserId);
                return !result.IsSuccess ? BadRequest(new { message = result.ErrorMessage }) : Ok(result.Data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting withdrawal limits");
                return StatusCode(500, new { message = "An error occurred while retrieving withdrawal limits" });
            }
        }

        [HttpGet("limits/user/{user}")]
        [Authorize(Roles = "ADMIN")]
        public async Task<IActionResult> GetWithdrawalLimits(string user)
        {
            try
            {
                if (string.IsNullOrEmpty(user) || !Guid.TryParse(user, out var userId))
                {
                    return BadRequest(new { message = "Invalid user ID" });
                }

                var result = await _withdrawalService.GetUserWithdrawalLimitsAsync(userId);
                return !result.IsSuccess ? BadRequest(new { message = result.ErrorMessage }) : Ok(result.Data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting withdrawal limits");
                return StatusCode(500, new { message = "An error occurred while retrieving withdrawal limits" });
            }
        }

        [HttpGet("networks/{assetTicker}")]
        public async Task<IActionResult> GetSupportedNetworks(string assetTicker)
        {
            try
            {
                if (string.IsNullOrEmpty(assetTicker))
                {
                    return BadRequest(new { message = "Invalid asset ticker" });
                }

                var result = await _withdrawalService.GetSupportedNetworksAsync(assetTicker);

                if (!result.IsSuccess)
                {
                    return BadRequest(new { message = result.ErrorMessage });
                }

                return Ok(new
                {
                    isSuccess = true,
                    data = result.Data
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting supported networks");
                return StatusCode(500, new { message = "An error occurred while retrieving supported networks" });
            }
        }

        [HttpGet("minimum/{assetTicker}")]
        public async Task<IActionResult> GetMinimumWithdrawalAmount(string assetTicker)
        {
            try
            {
                if (string.IsNullOrEmpty(assetTicker))
                {
                    return BadRequest(new { message = "Invalid asset ticker" });
                }

                var result = await _withdrawalService.GetMinimumWithdrawalThresholdAsync(assetTicker);

                if (!result.IsSuccess)
                {
                    return BadRequest(new { message = result.ErrorMessage });
                }

                return Ok(new
                {
                    isSuccess = true,
                    data = result.Data
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting supported networks");
                return StatusCode(500, new { message = "An error occurred while retrieving supported networks" });
            }
        }

        [HttpGet("pending/total/{assetTicker}")]
        [Authorize]
        public async Task<IActionResult> GetPendingTotals(string assetTicker)
        {
            try
            {
                if (string.IsNullOrEmpty(assetTicker))
                {
                    return BadRequest(new { message = "Invalid asset ticker" });
                }

                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var parsedUserId))
                {
                    return BadRequest(new { message = "Invalid user ID" });
                }

                var result = await _withdrawalService.GetUserPendingTotalsAsync(parsedUserId, assetTicker);

                if (result == null)
                    throw new Exception("Pending totals result retuned null");

                return result.ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting supported networks");
                return StatusCode(500, new { message = "An error occurred while retrieving supported networks" });
            }
        }

        [HttpPost("can-withdraw")]
        [Authorize]
        public async Task<IActionResult> CanUserWithdraw([FromBody] CanUserWithdrawRequest request)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var parsedUserId))
                {
                    return BadRequest(new { message = "Invalid user ID" });
                }

                var result = await _withdrawalService.CanUserWithdrawAsync(parsedUserId, request.Amount, request.Ticker);
                return !result.IsSuccess ? BadRequest(new { message = result.ErrorMessage }) : 
                    Ok(new {
                        data= result.Data,
                        message= result.DataMessage,
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error requesting withdrawal");
                return StatusCode(500, new { message = "An error occurred while processing withdrawal request" });
            }
        }

        [HttpPost("crypto/request")]
        [Authorize]
        public async Task<IActionResult> RequestWithdrawal([FromBody] CryptoWithdrawalRequest request)
        {
            if (request.UserId == Guid.Empty)
            {
                return BadRequest(new { message = "Invalid user ID" });
            }

            if (string.IsNullOrWhiteSpace(request.WithdrawalMethod) || !WithdrawalMethod.AllValues.Contains(request.WithdrawalMethod))
            {
                return BadRequest(new { message = "Invalid withdrawal method" });
            }

            if (string.IsNullOrWhiteSpace(request.Currency))
            {
                return BadRequest(new { message = "Invalid withdrawal method" });
            }

            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var parsedUserId))
                {
                    return BadRequest(new { message = "Invalid user ID" });
                }

                // Override the user ID with the authenticated user's ID for security
                request.UserId = parsedUserId;

                var result = await _withdrawalService.RequestCryptoWithdrawalAsync(request);

                if (result == null) throw new Exception("Unknown error");

                return result.ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error requesting withdrawal");
                return StatusCode(500, new { message = "An error occurred while processing withdrawal request" });
            }
        }

        [HttpGet("history")]
        [Authorize]
        public async Task<IActionResult> GetWithdrawalHistory()
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var parsedUserId))
                {
                    return BadRequest(new { message = "Invalid user ID" });
                }

                var result = await _withdrawalService.GetUserWithdrawalHistoryAsync(parsedUserId);
                if (result == null)
                    throw new Exception("Withdrawal history result retuned null");
                return result.ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting withdrawal history");
                return StatusCode(500, new { message = "An error occurred while retrieving withdrawal history" });
            }
        }

        [HttpGet("{withdrawalId}")]
        [Authorize]
        public async Task<IActionResult> GetWithdrawalDetails(string withdrawalId)
        {
            try
            {
                if (string.IsNullOrEmpty(withdrawalId) || !Guid.TryParse(withdrawalId, out var parsedWithdrawalId))
                {
                    return BadRequest(new { message = "Invalid withdrawal ID" });
                }

                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var parsedUserId))
                {
                    return BadRequest(new { message = "Invalid user ID" });
                }

                var result = await _withdrawalService.GetWithdrawalDetailsAsync(parsedWithdrawalId);
                if (!result.IsSuccess)
                {
                    return BadRequest(new { message = result.ErrorMessage });
                }

                // Ensure user can only see their own withdrawals (unless admin)
                return result.Data.UserId != parsedUserId && !User.IsInRole("ADMIN") ? Forbid() : Ok(result.Data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting withdrawal details");
                return StatusCode(500, new { message = "An error occurred while retrieving withdrawal details" });
            }
        }

        [HttpPut("{withdrawalId}/cancel")]
        [Authorize]
        public async Task<IActionResult> CancelWithdrawal(string withdrawalId)
        {
            try
            {
                if (string.IsNullOrEmpty(withdrawalId) || !Guid.TryParse(withdrawalId, out var parsedWithdrawalId))
                {
                    return BadRequest(new { message = "Invalid withdrawal ID" });
                }

                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var parsedUserId))
                {
                    return BadRequest(new { message = "Invalid user ID" });
                }

                // First get the withdrawal to check ownership
                var getResult = await _withdrawalService.GetWithdrawalDetailsAsync(parsedWithdrawalId);
                if (!getResult.IsSuccess)
                {
                    return BadRequest(new { message = getResult.ErrorMessage });
                }

                // Ensure user can only cancel their own withdrawals (unless admin)
                if (getResult.Data.UserId != parsedUserId && !User.IsInRole("ADMIN"))
                {
                    return Forbid();
                }

                // Only pending withdrawals can be canceled
                if (getResult.Data.Status != WithdrawalStatus.Pending)
                {
                    return BadRequest(new { message = $"Cannot cancel withdrawal in {getResult.Data.Status.ToLower()} status" });
                }

                // Update status to canceled
                var result = await _withdrawalService.UpdateWithdrawalStatusAsync(
                    parsedWithdrawalId,
                    WithdrawalStatus.Cancelled,
                    parsedUserId,
                    "Canceled by user");

                if (!result.IsSuccess)
                {
                    return BadRequest(new { message = result.ErrorMessage });
                }

                return Ok(new { message = "Withdrawal request canceled successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error canceling withdrawal");
                return StatusCode(500, new { message = "An error occurred while canceling withdrawal" });
            }
        }

        // Admin endpoints

        [HttpGet("pending/total/{assetTicker}/user/{userId}")]
        [Authorize(Roles = "ADMIN")]
        public async Task<IActionResult> GetPendingTotals(string userId, string assetTicker)
        {
            try
            {
                if (string.IsNullOrEmpty(assetTicker))
                {
                    return BadRequest(new { message = "Invalid asset ticker" });
                }

                if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var parsedUserId))
                {
                    return BadRequest(new { message = "Invalid user ID" });
                }

                var result = await _withdrawalService.GetUserPendingTotalsAsync(parsedUserId, assetTicker);

                if (result == null)
                    throw new Exception("Pending totals result retuned null");

                return result.ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting supported networks");
                return StatusCode(500, new { message = "An error occurred while retrieving supported networks" });
            }
        }

        [HttpGet("pending")]
        [Authorize(Roles = "ADMIN")]
        public async Task<IActionResult> GetPendingWithdrawals([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            try
            {
                var result = await _withdrawalService.GetPendingWithdrawalsAsync(page, pageSize);
                if (result == null)
                    throw new Exception("Pending withdrawals result returned null");
                return result.ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting pending withdrawals");
                return StatusCode(500, new { message = "An error occurred while retrieving pending withdrawals" });
            }
        }

        [HttpPut("{withdrawalId}/update-status")]
        [Authorize(Roles = "ADMIN")]
        public async Task<IActionResult> UpdateWithdrawalStatus(
            string withdrawalId,
            [FromBody] WithdrawalStatusUpdateRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(withdrawalId) || !Guid.TryParse(withdrawalId, out var parsedWithdrawalId))
                {
                    return BadRequest(new { message = "Invalid withdrawal ID" });
                }

                if (string.IsNullOrEmpty(request.Status))
                {
                    return BadRequest(new { message = "Status is required" });
                }

                if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
                {
                    return BadRequest(new { message = "Invalid user ID" });
                }

                var result = await _withdrawalService.UpdateWithdrawalStatusAsync(
                    parsedWithdrawalId,
                    request.Status,
                    userId,
                    request.Comment,
                    request.TransactionHash);

                if (!result.IsSuccess)
                {
                    return BadRequest(new { message = result.ErrorMessage });
                }

                return Ok(new { message = "Withdrawal status updated successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating withdrawal status");
                return StatusCode(500, new { message = "An error occurred while updating withdrawal status" });
            }
        }
    }
}