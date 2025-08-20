using Application.Contracts.Requests.KYC;
using Application.Extensions;
using Application.Interfaces.KYC;
using Domain.DTOs;
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
            var result = await _kycService.GetPendingVerificationsAsync(page, pageSize);

            return result.ToActionResult(this);
        }

        [HttpPost("update-status/{userId}")]
        public async Task<IActionResult> UpdateVerificationStatus(
            Guid userId,
            [FromBody] StatusUpdateRequest request)
        {
            var result = await _kycService.UpdateKycStatusAsync(
                    userId,
                    request.Status,
                    request.Comment);

            if(!result.IsSuccess || result.Data == null || !result.Data.IsSuccess)
                return ResultWrapper.Failure(
                    result.Reason,
                    result.ErrorMessage ?? "Failed to update KYC status.")
                    .ToActionResult(this);

            return ResultWrapper.Success($"KYC status successfully updated to {request.Status}")
                .ToActionResult(this);
        }
    }
}