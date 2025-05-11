// crypto_investment_project.Server/Controllers/KycController.cs
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
        private readonly IKycServiceFactory _kycServiceFactory;
        private readonly ILogger<KycController> _logger;

        public KycController(
            IKycServiceFactory kycServiceFactory,
            ILogger<KycController> logger)
        {
            _kycServiceFactory = kycServiceFactory ?? throw new ArgumentNullException(nameof(kycServiceFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [HttpGet("status")]
        [Authorize]
        [EnableRateLimiting("standard")]
        public async Task<IActionResult> GetUserKycStatus()
        {
            try
            {
                var userId = GetUserId();
                if (!userId.HasValue)
                {
                    return BadRequest(new { message = "Invalid user ID" });
                }

                var kycService = _kycServiceFactory.GetKycService();
                var result = await kycService.GetUserKycStatusAsync(userId.Value);

                if (!result.IsSuccess)
                {
                    return BadRequest(new { message = result.ErrorMessage });
                }

                return Ok(new { data = result.Data });
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
        public async Task<IActionResult> InitiateVerification([FromBody] KycVerificationRequest request, [FromQuery] string provider = null)
        {
            try
            {
                var userId = GetUserId();
                if (!userId.HasValue)
                {
                    return BadRequest(new { message = "Invalid user ID" });
                }

                // Ensure the requested verification is for the current user
                if (request.UserId != userId.Value && !User.IsInRole("ADMIN"))
                {
                    return Forbid();
                }

                // Get the appropriate KYC service based on the provider parameter or user ID
                IKycService kycService;
                if (!string.IsNullOrEmpty(provider) && User.IsInRole("ADMIN"))
                {
                    try
                    {
                        kycService = _kycServiceFactory.GetKycService(provider);
                    }
                    catch (ArgumentException)
                    {
                        return BadRequest(new { message = $"Invalid KYC provider: {provider}" });
                    }
                }
                else
                {
                    kycService = _kycServiceFactory.GetKycService();
                }

                var result = await kycService.InitiateKycVerificationAsync(request);
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

        [HttpPost("callback/{provider}")]
        [IgnoreAntiforgeryToken]
        [EnableRateLimiting("webhook")]
        public async Task<IActionResult> HandleKycCallback([FromRoute] string provider, [FromBody] KycCallbackRequest callback)
        {
            try
            {
                // Get the provider-specific KYC service
                IKycService kycService;
                try
                {
                    kycService = _kycServiceFactory.GetKycService(provider);
                }
                catch (ArgumentException)
                {
                    return BadRequest(new { message = $"Invalid KYC provider: {provider}" });
                }

                // Validate the callback signature
                if (Request.Headers.TryGetValue("X-Signature", out var signature))
                {
                    var payload = await ReadRequestBodyAsync();
                    var validationResult = await ((IKycProvider)kycService).ValidateCallbackSignature(signature, payload);

                    if (!validationResult.IsSuccess || !validationResult.Data)
                    {
                        return Unauthorized(new { message = "Invalid webhook signature" });
                    }
                }
                else
                {
                    return Unauthorized(new { message = "Missing webhook signature" });
                }

                // Process the callback
                var result = await kycService.ProcessKycCallbackAsync(callback);
                if (!result.IsSuccess)
                {
                    _logger.LogWarning($"KYC callback processing failed: {result.ErrorMessage}");
                    return BadRequest(new { message = result.ErrorMessage });
                }

                // After KYC verification, perform AML check
                var userId = result.Data.UserId;
                await kycService.PerformAmlCheckAsync(userId);

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
                // Use the default KYC service for admin operations
                var kycService = _kycServiceFactory.GetKycService();
                var result = await kycService.GetPendingVerificationsAsync(page, pageSize);

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

                // Get the appropriate KYC service
                var kycService = _kycServiceFactory.GetKycService();
                var result = await kycService.UpdateKycStatusAsync(
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
                var userId = GetUserId();
                if (!userId.HasValue)
                {
                    return BadRequest(new { message = "Invalid user ID" });
                }

                var kycService = _kycServiceFactory.GetKycService();
                var result = await kycService.IsUserEligibleForTrading(userId.Value);

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

        [HttpGet("providers")]
        [Authorize(Roles = "ADMIN")]
        public IActionResult GetAvailableProviders()
        {
            try
            {
                return Ok(new { providers = new[] { "Onfido", "SumSub" } });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting available KYC providers");
                return StatusCode(500, new { message = "An error occurred while retrieving KYC providers" });
            }
        }

        private Guid? GetUserId()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var parsedUserId))
            {
                return null;
            }
            return parsedUserId;
        }

        private async Task<string> ReadRequestBodyAsync()
        {
            using var reader = new StreamReader(Request.Body, leaveOpen: true);
            var body = await reader.ReadToEndAsync();

            // Reset the request body position for potential future reads
            Request.Body.Position = 0;

            return body;
        }
    }

    public class KycStatusUpdateRequest
    {
        public string Status { get; set; }
        public string Comment { get; set; }
    }
}