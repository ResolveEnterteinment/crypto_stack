using Application.Extensions;
using Application.Interfaces.Logging;
using Domain.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace crypto_investment_project.Server.Controllers
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/[controller]")]
    [Authorize(Roles = "ADMIN")]
    [Produces("application/json")]
    public class TraceController : ControllerBase
    {
        private readonly ILogExplorerService _logExplorerService;
        private readonly ILoggingService _logger;

        public TraceController(
            ILogExplorerService logExplorerService,
            ILoggingService logger)
        {
            _logExplorerService = logExplorerService ?? throw new ArgumentNullException(nameof(logExplorerService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [HttpGet]
        [Route("tree")]
        [EnableRateLimiting("standard")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetTraceTree()
        {
            try
            {
                // Get user subscriptions
                var traceResult = await _logExplorerService.GetTraceTreeAsync();

                _logger.LogInformation("Successfully retrieved trace logs.");

                return Ok(traceResult);
            }
            catch (Exception ex)
            {
                return ResultWrapper.FromException(ex).ToActionResult(this);
            }
        }

        [HttpPost]
        [Route("resolve/{id}")]
        [EnableRateLimiting("standard")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ResolveTrace(
            string id,
            [FromBody] string comment)
        {
            try
            {
                if (!Guid.TryParse(id, out var parsedId))
                    throw new ArgumentException("Invalid trace ID");

                if (string.IsNullOrWhiteSpace(comment))
                    throw new ArgumentException("Resolution comment is required.");

                var sanitized = Regex.Replace(comment, @"<[^>]*>", string.Empty);

                // Remove control characters which could cause issues in logs or databases
                sanitized = Regex.Replace(sanitized, @"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", string.Empty);

                // Trim excessive whitespace
                sanitized = Regex.Replace(sanitized, @"\s+", " ").Trim();

                var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                await _logExplorerService.Resolve(parsedId, sanitized, Guid.Parse(currentUserId));
                _logger.LogInformation($"Successfully resolved log {id}.");
                return Ok();
            }
            catch (Exception ex)
            {
                return ResultWrapper.FromException(ex).ToActionResult(this);
            }
        }
    }
}