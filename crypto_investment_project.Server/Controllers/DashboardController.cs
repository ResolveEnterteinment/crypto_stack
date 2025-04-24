using Application.Extensions;
using Application.Interfaces;
using Domain.Constants;
using Domain.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

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

        [HttpGet]
        [Route("user/{user}")]
        //[IgnoreAntiforgeryToken]
        [Authorize(Roles = "USER")]
        [EnableRateLimiting("standard")]
        public async Task<IActionResult> GetUserDashboardData(string user)
        {
            if (!Guid.TryParse(user, out var userId) || userId == Guid.Empty)
            {
                return ResultWrapper.Failure(FailureReason.ValidationError, "Invalid user id.").ToActionResult(this);
            }
            // Authorization check - verify current user can create this subscription
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var isAdmin = User.IsInRole("ADMIN");

            if (!isAdmin && user != currentUserId)
            {
                _logger.LogWarning("Unauthorized attempt to get dashboard data for user {TargetUserId} by user {CurrentUserId}",
                    user, currentUserId);
                return Forbid();
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var dashboardResult = await _dashboardService.GetDashboardDataAsync(userId);
            if (!dashboardResult.IsSuccess)
            {
                return dashboardResult.ToActionResult(this);
            }
            _logger.LogInformation("Dashboard data fetched for {UserId} in {ElapsedMs}ms", userId, stopwatch.ElapsedMilliseconds);
            return Ok(dashboardResult.Data);
        }
    }
}