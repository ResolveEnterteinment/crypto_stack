using Application.Extensions;
using Application.Interfaces;
using Domain.Constants;
using Domain.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace crypto_investment_project.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DashboardController : ControllerBase
    {
        private readonly IDashboardService _dashboardService;
        private readonly ILogger<DashboardController> _logger;

        public DashboardController(IDashboardService dashboardService, ILogger<DashboardController> logger)
        {
            _dashboardService = dashboardService;
            _logger = logger;
        }

        [HttpPost("{userId}")]
        [Authorize(Roles = "USER")]
        [EnableRateLimiting("standard")]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> GetUserDashboardData(string userId)
        {
            if (!Guid.TryParse(userId, out var userIdParsed) || userIdParsed == Guid.Empty)
            {
                ResultWrapper.Failure(FailureReason.ValidationError, "Invalid user id.").ToActionResult(this);
            }
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var data = await _dashboardService.GetDashboardDataAsync(userIdParsed);
            _logger.LogInformation("Dashboard data fetched for {UserId} in {ElapsedMs}ms", userId, stopwatch.ElapsedMilliseconds);
            return Ok(data);
        }
    }
}