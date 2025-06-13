using Application.Interfaces.Logging;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace crypto_investment_project.Server.Controllers.Auth
{
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/[controller]")]
    [Produces("application/json")]
    public class CsrfController : ControllerBase
    {
        private readonly IAntiforgery _antiforgery;
        private readonly ILoggingService _logger;

        public CsrfController(
            IAntiforgery antiforgery,
            ILoggingService logger
            )
        {
            _antiforgery = antiforgery;
            _logger = logger;
        }

        [HttpGet]
        [Route("refresh")]
        public IActionResult GetToken()
        {
            var tokens = _antiforgery.GetAndStoreTokens(HttpContext);

            Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken, new CookieOptions
            {
                HttpOnly = false,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Path = "/" // Ensure this explicitly to avoid path issues
            });

            return Ok(new { token = tokens.RequestToken });
        }

        /// <summary>
        /// Validates a CSRF token (for testing purposes)
        /// </summary>
        /// <returns>Validation result</returns>
        [HttpPost("validate")]
        [AllowAnonymous]
        public async Task<IActionResult> ValidateToken()
        {
            try
            {
                _logger.LogInformation("🔍 CSRF token validation requested");

                await _antiforgery.ValidateRequestAsync(HttpContext);

                _logger.LogInformation("✅ CSRF token validation successful");
                return Ok(new
                {
                    valid = true,
                    message = "CSRF token is valid",
                    timestamp = DateTime.UtcNow
                });
            }
            catch (AntiforgeryValidationException ex)
            {
                _logger.LogWarning($"⚠️ CSRF token validation failed: {ex.Message}");
                return BadRequest(new
                {
                    valid = false,
                    error = "Invalid CSRF token",
                    message = ex.Message,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ CSRF token validation error: {ex.Message}");
                return StatusCode(500, new
                {
                    error = "CSRF validation error",
                    message = ex.Message
                });
            }
        }

        /// <summary>
        /// Simple endpoint to test CSRF setup
        /// </summary>
        [HttpGet("test")]
        [AllowAnonymous]
        public IActionResult Test()
        {
            return Ok(new
            {
                message = "CSRF controller is working",
                timestamp = DateTime.UtcNow,
                endpoints = new[]
                {
                    "GET /api/v1/csrf/refresh - Get new CSRF token",
                    "GET /api/v1/csrf/token - Get current token",
                    "POST /api/v1/csrf/validate - Validate token"
                }
            });
        }
    }
}