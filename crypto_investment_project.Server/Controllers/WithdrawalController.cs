// crypto_investment_project.Server/Controllers/WithdrawalController.cs
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
        private readonly ILogger<WithdrawalController> _logger;

        public WithdrawalController(
            IWithdrawalService withdrawalService,
            ILogger<WithdrawalController> logger)
        {
            _withdrawalService = withdrawalService ?? throw new ArgumentNullException(nameof(withdrawalService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [HttpGet("limits")]
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
                if (!result.IsSuccess)
                {
                    return BadRequest(new { message = result.ErrorMessage });
                }

                return Ok(result.Data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting withdrawal limits");
                return StatusCode(500, new { message = "An error occurred while retrieving withdrawal limits" });
            }
        }

        [HttpPost("request")]
        public async Task<IActionResult> RequestWithdrawal([FromBody] WithdrawalRequest request)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var parsedUserId))
                {
                    return BadRequest(new { message = "Invalid user ID" });
                }

                // Override the user ID with the authenticated user's ID for security
                request.UserId = parsedUserId;

                var result = await _withdrawalService.RequestWithdrawalAsync(request);
                if (!result.IsSuccess)
                {
                    return BadRequest(new { message = result.ErrorMessage });
                }

                return Ok(result.Data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error requesting withdrawal");
                return StatusCode(500, new { message = "An error occurred while processing withdrawal request" });
            }
        }

        [HttpGet("history")]
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
                if (!result.IsSuccess)
                {
                    return BadRequest(new { message = result.ErrorMessage });
                }

                return Ok(result.Data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting withdrawal history");
                return StatusCode(500, new { message = "An error occurred while retrieving withdrawal history" });
            }
        }

        [HttpGet("{withdrawalId}")]
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
                if (result.Data.UserId != parsedUserId && !User.IsInRole("ADMIN"))
                {
                    return Forbid();
                }

                return Ok(result.Data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting withdrawal details");
                return StatusCode(500, new { message = "An error occurred while retrieving withdrawal details" });
            }
        }

        [HttpPut("{withdrawalId}/cancel")]
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
        [HttpGet("admin/pending")]
        [Authorize(Roles = "ADMIN")]
        public async Task<IActionResult> GetPendingWithdrawals([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            try
            {
                var result = await _withdrawalService.GetPendingWithdrawalsAsync(page, pageSize);
                if (!result.IsSuccess)
                {
                    return BadRequest(new { message = result.ErrorMessage });
                }

                return Ok(result.Data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting pending withdrawals");
                return StatusCode(500, new { message = "An error occurred while retrieving pending withdrawals" });
            }
        }

        [HttpPut("admin/{withdrawalId}/update-status")]
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

                var result = await _withdrawalService.UpdateWithdrawalStatusAsync(
                    parsedWithdrawalId,
                    request.Status,
                    request.Comment);

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

    public class WithdrawalStatusUpdateRequest
    {
        public string Status { get; set; }
        public string Comment { get; set; }
    }
}