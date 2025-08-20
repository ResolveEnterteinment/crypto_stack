using Application.Contracts.Responses.Csrf;
using Application.Extensions;
using Application.Interfaces.Logging;
using Domain.Constants;
using Domain.DTOs;
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

            return ResultWrapper.Success(
                new CsrfTokenResponse
                {
                    Token = tokens.RequestToken
                })
                .ToActionResult(this);
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

                return ResultWrapper.Success("CSRF token is valid").ToActionResult(this);
            }
            catch (AntiforgeryValidationException ex)
            {
                _logger.LogWarning($"⚠️ CSRF token validation failed: {ex.Message}");

                return ResultWrapper.Failure(
                    FailureReason.ValidationError,
                    "Invalid CSRF token")
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ CSRF token validation error: {ex.Message}");

                return ResultWrapper.InternalServerError()
                    .ToActionResult(this);
            }
        }

        /// <summary>
        /// Simple endpoint to test CSRF setup
        /// </summary>
        [HttpGet("test")]
        [AllowAnonymous]
        public IActionResult Test()
        {
            return ResultWrapper.Success(new[]
                {
                    "GET /api/v1/csrf/refresh - Get new CSRF token",
                    "GET /api/v1/csrf/token - Get current token",
                    "POST /api/v1/csrf/validate - Validate token"
                },"CSRF controller is working").ToActionResult(this);
        }
    }
}