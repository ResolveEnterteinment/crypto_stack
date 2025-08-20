using Application.Extensions;
using Application.Interfaces;
using CryptoExchange.Net.CommonObjects;
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
        [Authorize]
        [EnableRateLimiting("standard")]
        public async Task<IActionResult> GetUserDashboardData()
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!Guid.TryParse(currentUserId, out var userId) || userId == Guid.Empty)
            {
                return ResultWrapper.Failure(FailureReason.ValidationError, "Invalid user id.").ToActionResult(this);
            }

            try
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                var dashboardResult = await _dashboardService.GetDashboardDataAsync(userId);

                if (dashboardResult == null || !dashboardResult.IsSuccess || dashboardResult.Data == null)
                {
                    return ResultWrapper.NotFound("Dashboard")
                        .ToActionResult(this);
                }
                
                stopwatch.Stop();

                _logger.LogInformation("Dashboard data fetched for {UserId} in {ElapsedMs}ms", userId, stopwatch.ElapsedMilliseconds);

                return ResultWrapper.Success(dashboardResult.Data).ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving user ID {userId} dashboard");

                return ResultWrapper.InternalServerError()
                .ToActionResult(this);
            }
        }
    }
}