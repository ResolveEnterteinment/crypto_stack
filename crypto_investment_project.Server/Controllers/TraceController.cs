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
        public async Task<IActionResult> GetTraceTree(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] int filterLevel = 0,
            [FromQuery] Guid? rootId = null)
        {
            try
            {
                // Validate pagination parameters
                if (page < 1)
                {
                    return ResultWrapper.ValidationError(
                        new Dictionary<string, string[]> { ["page"] = new[] { "Page must be greater than 0" } })
                        .ToActionResult(this);
                }

                if (pageSize < 1 || pageSize > 100)
                {
                    return ResultWrapper.ValidationError(
                        new Dictionary<string, string[]> { ["pageSize"] = new[] { "Page size must be between 1 and 100" } })
                        .ToActionResult(this);
                }

                // Get paginated trace tree
                var traceResult = await _logExplorerService.GetTraceTreePaginatedAsync(page, pageSize, filterLevel, rootId);

                if (!traceResult.IsSuccess)
                {
                    _logger.LogError($"Failed to retrieve trace logs: {traceResult.ErrorMessage}");
                    return traceResult.ToActionResult(this);
                }

                _logger.LogInformation($"Successfully retrieved {traceResult.Data.Items.Count()} trace logs on page {page}.");

                return traceResult.ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to retrieve trace logs: {ex.Message}");
                return ResultWrapper.InternalServerError()
                        .ToActionResult(this);
            }
        }

        // Keep the original non-paginated endpoint for backward compatibility
        [HttpGet]
        [Route("tree/all")]
        [EnableRateLimiting("standard")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetAllTraceTree()
        {
            try
            {
                var traceResult = await _logExplorerService.GetTraceTreeAsync();

                _logger.LogInformation("Successfully retrieved all trace logs.");

                return ResultWrapper.Success(traceResult)
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to retrieve trace logs: {ex.Message}");
                return ResultWrapper.InternalServerError()
                        .ToActionResult(this);
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

                return ResultWrapper.Success($"Successfully resolved log {id}.")
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to resolve log {id}: {ex.Message}");
                return ResultWrapper.InternalServerError()
                        .ToActionResult(this);
            }
        }

        [HttpDelete]
        [Route("purge")]
        [EnableRateLimiting("standard")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PurgeLogs([FromQuery] int maxLevel)
        {
            try
            {
                // Validate the log level parameter
                if (maxLevel < 0 || maxLevel > 5)
                {
                    return ResultWrapper.ValidationError(
                        new Dictionary<string, string[]>
                        {
                            ["maxLevel"] = new[] { "Log level must be between 0 (Trace) and 5 (Critical)" }
                        },
                        "Invalid log level specified")
                        .ToActionResult(this);
                }

                var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                _logger.LogInformation($"Admin user {currentUserId} initiated log purge operation for level {maxLevel} and below.");

                // Execute the purge operation
                var purgeResult = await _logExplorerService.PurgeLogsAsync(maxLevel);

                if (!purgeResult.IsSuccess)
                {
                    _logger.LogError($"Failed to purge logs with level {maxLevel} and below: {purgeResult.ErrorMessage}");
                    return purgeResult.ToActionResult(this);
                }

                var deletedCount = purgeResult.Data.ModifiedCount;
                _logger.LogInformation($"Successfully purged {deletedCount} log(s) with level {maxLevel} and below by admin user {currentUserId}.");

                return ResultWrapper.Success($"{deletedCount} logs successfully deleted.")
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to purge logs with level {maxLevel} and below: {ex.Message}");
                return ResultWrapper.InternalServerError()
                        .ToActionResult(this);
            }
        }
    }
}