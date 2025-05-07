// crypto_investment_project.Server/Controllers/KycController.cs
using Application.Contracts.Requests.KYC;
using Application.Interfaces.KYC;
using Domain.DTOs.KYC;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace crypto_investment_project.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class KycController : ControllerBase
    {
        private readonly IKycService _kycService;
        private readonly ILogger<KycController> _logger;

        public KycController(
            IKycService kycService,
            ILogger<KycController> logger)
        {
            _kycService = kycService ?? throw new ArgumentNullException(nameof(kycService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [HttpGet("status")]
        [Authorize]
        [EnableRateLimiting("standard")]
        public async Task<IActionResult> GetUserKycStatus()
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var parsedUserId))
                {
                    return BadRequest(new { message = "Invalid user ID" });
                }

                var result = await _kycService.GetUserKycStatusAsync(parsedUserId);
                if (!result.IsSuccess)
                {
                    return BadRequest(new { message = result.ErrorMessage });
                }

                return Ok(result.Data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting KYC status");
                return StatusCode(500, new { message = "An error occurred while retrieving KYC status" });
            }
        }

        [HttpPost("verify")]
        [Authorize]
        [EnableRateLimiting("standard")]
        public async Task<IActionResult> InitiateVerification([FromBody] KycVerificationRequest request)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var parsedUserId))
                {
                    return BadRequest(new { message = "Invalid user ID" });
                }

                // Ensure the requested verification is for the current user
                if (request.UserId != parsedUserId && !User.IsInRole("ADMIN"))
                {
                    return Forbid();
                }

                var result = await _kycService.InitiateKycVerificationAsync(request);
                if (!result.IsSuccess)
                {
                    return BadRequest(new { message = result.ErrorMessage });
                }

                return Ok(result.Data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initiating KYC verification");
                return StatusCode(500, new { message = "An error occurred while initiating KYC verification" });
            }
        }

        [HttpPost("callback")]
        [IgnoreAntiforgeryToken]
        [EnableRateLimiting("webhook")]
        public async Task<IActionResult> HandleKycCallback([FromBody] KycCallbackRequest callback)
        {
            try
            {
                // Validate the callback is authentic (e.g., with a webhook token)
                if (!Request.Headers.TryGetValue("X-Signature", out var signature))
                {
                    return Unauthorized(new { message = "Missing webhook signature" });
                }

                // Process the callback
                var result = await _kycService.ProcessKycCallbackAsync(callback);
                if (!result.IsSuccess)
                {
                    _logger.LogWarning($"KYC callback processing failed: {result.ErrorMessage}");
                    return BadRequest(new { message = result.ErrorMessage });
                }

                return Ok(new { message = "Callback processed successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing KYC callback");
                return StatusCode(500, new { message = "An error occurred while processing KYC callback" });
            }
        }

        [HttpGet("pending")]
        [Authorize(Roles = "ADMIN")]
        [EnableRateLimiting("standard")]
        public async Task<IActionResult> GetPendingVerifications([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            try
            {
                // crypto_investment_project.Server/Controllers/KycController.cs (continued)
                var result = await _kycService.GetPendingVerificationsAsync(page, pageSize);
                if (!result.IsSuccess)
                {
                    return BadRequest(new { message = result.ErrorMessage });
                }

                return Ok(result.Data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting pending KYC verifications");
                return StatusCode(500, new { message = "An error occurred while retrieving pending verifications" });
            }
        }

        [HttpPost("admin/update/{userId}")]
        [Authorize(Roles = "ADMIN")]
        [EnableRateLimiting("standard")]
        public async Task<IActionResult> UpdateKycStatus(
            string userId,
            [FromBody] KycStatusUpdateRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var parsedUserId))
                {
                    return BadRequest(new { message = "Invalid user ID" });
                }

                var result = await _kycService.UpdateKycStatusAsync(
                    parsedUserId,
                    request.Status,
                    request.Comment);

                if (!result.IsSuccess)
                {
                    return BadRequest(new { message = result.ErrorMessage });
                }

                return Ok(new { message = "KYC status updated successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating KYC status");
                return StatusCode(500, new { message = "An error occurred while updating KYC status" });
            }
        }

        [HttpGet("check-eligibility")]
        [Authorize]
        [EnableRateLimiting("standard")]
        public async Task<IActionResult> CheckTradingEligibility()
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var parsedUserId))
                {
                    return BadRequest(new { message = "Invalid user ID" });
                }

                var result = await _kycService.IsUserEligibleForTrading(parsedUserId);
                if (!result.IsSuccess)
                {
                    return BadRequest(new { message = result.ErrorMessage });
                }

                return Ok(new { isEligible = result.Data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking trading eligibility");
                return StatusCode(500, new { message = "An error occurred while checking trading eligibility" });
            }
        }
    }
}