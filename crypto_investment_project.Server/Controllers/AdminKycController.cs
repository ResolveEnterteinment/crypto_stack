using Application.Interfaces.KYC;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace crypto_investment_project.Server.Controllers
{
    [ApiController]
    [Route("api/admin/kyc")]
    [Authorize(Roles = "ADMIN")]
    public class AdminKycController : ControllerBase
    {
        private readonly IKycService _kycService;
        private readonly ILogger<AdminKycController> _logger;

        public AdminKycController(
            IKycService kycService,
            ILogger<AdminKycController> logger)
        {
            _kycService = kycService;
            _logger = logger;
        }

        [HttpGet("pending")]
        public async Task<IActionResult> GetPendingVerifications([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            try
            {
                var result = await _kycService.GetPendingVerificationsAsync(page, pageSize);
                if (!result.IsSuccess)
                {
                    return BadRequest(new { success = false, message = result.ErrorMessage });
                }

                return Ok(new { success = true, data = result.Data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving pending KYC verifications");
                return StatusCode(500, new { success = false, message = "An error occurred retrieving pending verifications" });
            }
        }

        [HttpPost("update-status/{userId}")]
        public async Task<IActionResult> UpdateVerificationStatus(
            Guid userId,
            [FromBody] StatusUpdateRequest request)
        {
            try
            {
                var result = await _kycService.UpdateKycStatusAsync(
                    userId,
                    request.Status,
                    request.Comment);

                if (!result.IsSuccess)
                {
                    return BadRequest(new { success = false, message = result.ErrorMessage });
                }

                return Ok(new { success = true, message = $"KYC status updated to {request.Status}" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating KYC status for user {userId}");
                return StatusCode(500, new { success = false, message = "An error occurred updating KYC status" });
            }
        }
    }

    public class StatusUpdateRequest
    {
        public required string Status { get; set; }
        public string? Comment { get; set; }
    }
}