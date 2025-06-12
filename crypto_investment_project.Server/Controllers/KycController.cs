using Application.Interfaces.KYC;
using Domain.DTOs;
using Domain.DTOs.KYC;
using Domain.Models.KYC;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace crypto_investment_project.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class KycController : ControllerBase
    {
        private readonly IKycService _kycService;
        private readonly ILogger<KycController> _logger;

        public KycController(
            IKycService kycService,
            ILogger<KycController> logger)
        {
            _kycService = kycService;
            _logger = logger;
        }

        [HttpPost("verify")]
        [Authorize]
        [EnableRateLimiting("standard")]
        public async Task<IActionResult> ProcessVerification([FromBody] VerificationRequest request)
        {
            try
            {
                Guid? userId = GetUserId();
                if (!userId.HasValue)
                {
                    return BadRequest(new { success = false, message = "Invalid user ID" });
                }

                // Ensure the requested verification is for the current user
                if (request.UserId != userId.Value.ToString() && !User.IsInRole("ADMIN"))
                {
                    return Forbid();
                }

                ResultWrapper<KycData> result = await _kycService.VerifyAsync(new KycVerificationRequest
                {
                    UserId = Guid.Parse(request.UserId),
                    SessionId = Guid.Parse(request.SessionId),
                    VerificationLevel = request.VerificationLevel,
                    Data = request.Data
                });

                if (!result.IsSuccess)
                {
                    return BadRequest(new { success = false, message = result.ErrorMessage });
                }

                // Perform AML check
                _ = await _kycService.PerformAmlCheckAsync(userId.Value);


                return Ok(new { success = true, message = "Verification processed successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing minimal verification");
                return StatusCode(500, new { success = false, message = "An error occurred during verification" });
            }
        }

        [HttpPost("session")]
        [Authorize]
        [EnableRateLimiting("standard")]
        public async Task<IActionResult> CreateSession([FromBody] SessionRequest request)
        {
            try
            {
                Guid? userId = GetUserId();
                if (!userId.HasValue)
                {
                    return BadRequest(new { success = false, message = "Invalid user ID" });
                }

                ResultWrapper<KycSessionData> result = await _kycService.GetOrCreateUserSessionAsync(
                    userId.Value,
                    request.VerificationLevel);

                if (!result.IsSuccess)
                {
                    return BadRequest(new { success = false, message = result.ErrorMessage });
                }

                return Ok(new
                {
                    success = true,
                    data = result.Data.Id
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating KYC session");
                return StatusCode(500, new { success = false, message = "An error occurred creating KYC session" });
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

    public class VerificationRequest
    {
        public required string UserId { get; set; }
        public required string SessionId { get; set; }
        public required string VerificationLevel { get; set; }
        public required Dictionary<string, object> Data { get; set; }
    }

    public class SessionRequest
    {
        public string VerificationLevel { get; set; } = "STANDARD";
    }
}