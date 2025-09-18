using Application.Contracts.Requests.Auth;
using Application.Contracts.Responses;
using Application.Contracts.Responses.Auth;
using Application.Extensions;
using Application.Interfaces;
using Domain.Constants;
using Domain.DTOs;
using Domain.DTOs.Error;
using Domain.Models.Authentication;
using Domain.Utilities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace crypto_investment_project.Server.Controllers.Auth
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/auth")]
    [Produces("application/json")]
    public class AuthenticationController : ControllerBase
    {
        private readonly Application.Interfaces.IAuthenticationService _authenticationService;
        private readonly RoleManager<ApplicationRole> _roleManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IUserService _userService;
        private readonly ILogger<AuthenticationController> _logger;
        private readonly HtmlEncoder _htmlEncoder;

        public AuthenticationController(
            RoleManager<ApplicationRole> roleManager,
            UserManager<ApplicationUser> userManager,
            IUserService userService,
        Application.Interfaces.IAuthenticationService authenticationService,
            ILogger<AuthenticationController> logger,
            HtmlEncoder htmlEncoder)
        {
            _roleManager = roleManager ?? throw new ArgumentNullException(nameof(roleManager));
            _userManager = userManager ?? throw new ArgumentException(nameof(userManager));
            _userService = userService ?? throw new ArgumentException(nameof(userService));
            _authenticationService = authenticationService ?? throw new ArgumentNullException(nameof(authenticationService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _htmlEncoder = htmlEncoder ?? throw new ArgumentNullException(nameof(htmlEncoder));
        }

        /// <summary>
        /// Creates a new role in the system
        /// </summary>
        /// <param name="request">Role creation request details</param>
        /// <returns>Result of role creation operation</returns>
        /// <response code="200">Role created successfully</response>
        /// <response code="400">Invalid role name</response>
        /// <response code="401">Unauthorized access</response>
        /// <response code="403">Insufficient permissions</response>
        /// <response code="409">Role already exists</response>
        /// <response code="500">Internal server error</response>
        [HttpPost]
        [Route("roles")]
        [Authorize(Roles = "ADMIN")]
        [ProducesResponseType(typeof(BaseResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> CreateRole([FromBody] CreateRoleRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return ResultWrapper.Failure(FailureReason.ValidationError,
                        "Invalid role data provided",
                        "INVALID_REQUEST",
                        ModelState.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray()))
                        .ToActionResult(this);
                }

                // Sanitize role name for security
                string sanitizedRoleName = _htmlEncoder.Encode(request.Role.Trim());

                // Check if role already exists
                if (await _roleManager.RoleExistsAsync(sanitizedRoleName))
                {
                    return ResultWrapper.Failure(FailureReason.ConcurrencyConflict,
                        $"Role '{sanitizedRoleName}' already exists",
                        "ROLE_ALREADY_EXISTS",
                        ModelState.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray()))
                        .ToActionResult(this);
                }

                var appRole = new ApplicationRole { Name = sanitizedRoleName };
                var createRoleResult = await _roleManager.CreateAsync(appRole);

                if (!createRoleResult.Succeeded)
                {
                    return ResultWrapper.Failure(FailureReason.Unknown,
                        "Failed to create role",
                        "ROLE_CREATION_FAILED",
                        new Dictionary<string, string[]>
                        {
                            ["Errors"] = createRoleResult.Errors.Select(e => e.Description).ToArray()
                        })
                        .ToActionResultWithStatusCode(this, 500);
                }

                _logger.LogInformation("Role {RoleName} created successfully by {UserId}",
                    sanitizedRoleName, User.FindFirstValue(ClaimTypes.NameIdentifier));

                return ResultWrapper.Success($"Role '{sanitizedRoleName}' created successfully")
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating role {RoleName}", request.Role);

                return ResultWrapper.InternalServerError()
                    .ToActionResult(this);
            }
        }

        /// <summary>
        /// Registers a new user account
        /// </summary>
        /// <param name="request">User registration details</param>
        /// <returns>Result of registration operation</returns>
        /// <response code="200">Registration successful or failed with validation errors</response>
        /// <response code="400">Invalid registration data</response>
        /// <response code="429">Too many registration attempts</response>
        /// <response code="500">Internal server error</response>
        [HttpPost]
        [Route("register")]
        [EnableRateLimiting("AuthEndpoints")]
        [IgnoreAntiforgeryToken]
        [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return ResultWrapper.Failure(FailureReason.ValidationError,
                        "Invalid registration data",
                        "INVALID_REQUEST",
                        ModelState.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray()))
                        .ToActionResult(this);
                }

                // Get client IP for rate limiting/security
                string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                _logger.LogInformation("Registration attempt from IP {ClientIP}", clientIp);

                var result = await _authenticationService.RegisterAsync(request);

                // Even if registration fails due to duplicate email or other validation,
                // we return 200 with failure details to prevent user enumeration attacks

                return ResultWrapper.Success(result)
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during registration");

                return ResultWrapper.InternalServerError()
                    .ToActionResult(this);
            }
        }

        /// <summary>
        /// Authenticates a user and issues JWT token
        /// </summary>
        /// <param name="request">Login credentials</param>
        /// <returns>Authentication result with tokens if successful</returns>
        /// <response code="200">Login successful with tokens</response>
        /// <response code="400">Invalid login data</response>
        /// <response code="401">Invalid credentials or account locked</response>
        /// <response code="429">Too many login attempts</response>
        /// <response code="500">Internal server error</response>
        [HttpPost("login")]
        [IgnoreAntiforgeryToken]
        [EnableRateLimiting("AuthEndpoints")]
        [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Login(
            [FromBody] LoginRequest request,
            [FromServices] CacheWarmupService cacheWarmup)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    if (!ModelState.IsValid)
                    {
                        return ResultWrapper.Failure(FailureReason.ValidationError,
                            "Invalid login data",
                            "INVALID_REQUEST",
                            ModelState.ToDictionary(
                                kvp => kvp.Key,
                                kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray()))
                            .ToActionResult(this);
                    }
                }

                // Get client IP for rate limiting/security
                string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                _logger.LogInformation("Login attempt from IP {ClientIP}", clientIp);

                var loginResult = await _authenticationService.LoginAsync(request);

                if (!loginResult.IsSuccess)
                {
                    // Return 401 for authentication failures
                    return loginResult.ToActionResultWithStatusCode(this, 401);
                }

                var result = loginResult.Data;

                // Queue cache warmup - this is why you need singleton registration
                cacheWarmup.QueueUserCacheWarmup(Guid.Parse(result.UserId));

                // Set refresh token in HTTP-only cookie for better security
                SetRefreshTokenCookie(result.RefreshToken);

                // Return success response with access token
                return ResultWrapper.Success(new LoginResponse {
                    AccessToken = result.AccessToken,
                    RefreshToken = result.RefreshToken,
                    UserId = DataMaskingUtility.MaskAlphanumeric(result.UserId.ToString()),
                    Username = DataMaskingUtility.MaskFullName(result.Username),
                    EmailConfirmed = result.EmailConfirmed
                    // Note: RefreshToken deliberately not included in response body
                }).ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login");

                return ResultWrapper.InternalServerError()
                    .ToActionResult(this);
            }
        }

        /// <summary>
        /// Get current authenticated user information
        /// </summary>
        /// <returns>Current user data</returns>
        /// <response code="200">Returns the user information</response>
        /// <response code="401">If the user is not authenticated</response>
        [HttpGet]
        [Route("user")]
        [Authorize]
        [ProducesResponseType(typeof(UserDataResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetCurrentUser([FromServices] CacheWarmupService cacheWarmup)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                {
                    return ResultWrapper.Failure(FailureReason.Unauthorized,
                            "User identity not found",
                            "USER_NOT_FOUND")
                            .ToActionResult(this);
                }

                var user = await _userManager.FindByIdAsync(userId);
                if (user == null)
                {
                    return ResultWrapper.Failure(FailureReason.Unauthorized,
                            "User not found",
                            "USER_NOT_FOUND")
                            .ToActionResult(this);
                }

                var roles = await _userManager.GetRolesAsync(user);

                // Get user data
                var userData = await _userService.GetByIdAsync(user.Id);

                // Queue cache warmup - this is why you need singleton registration
                cacheWarmup.QueueUserCacheWarmup(Guid.Parse(userId));

                return ResultWrapper.Success(new UserDataResponse
                {
                    Success = true,
                    Id = user.Id.ToString(),
                    Email = DataMaskingUtility.MaskEmail(user.Email),
                    Username = DataMaskingUtility.MaskFullName(user.UserName),
                    EmailConfirmed = user.EmailConfirmed,
                    Roles = roles.ToArray(),
                    IsFirstLogin = !user.HasCompletedOnboarding
                }).ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving current user");

                return ResultWrapper.InternalServerError()
                    .ToActionResult(this);
            }
        }

        /// <summary>
        /// Confirms a user's email address
        /// </summary>
        /// <param name="token">Combined token containing userId and confirmation token</param>
        /// <returns>Result of the confirmation operation</returns>
        [HttpPost("confirm-email")]
        [IgnoreAntiforgeryToken]
        [AllowAnonymous]
        [EnableRateLimiting("AuthEndpoints")]
        public async Task<IActionResult> ConfirmEmail([FromBody] ConfirmEmailRequest request)
        {
            if (string.IsNullOrEmpty(request.Token))
            {
                return ResultWrapper.Failure(FailureReason.ValidationError,
                        "Invalid confirmation token",
                        "INVALID_TOKEN")
                        .ToActionResult(this);
            }

            try
            {
                // Split the combined token to extract userId and the actual token
                string[] tokenParts = request.Token.Split(':', 2);
                if (tokenParts.Length != 2)
                {
                    return ResultWrapper.Failure(FailureReason.ValidationError,
                        "Invalid token format",
                        "INVALID_TOKEN")
                        .ToActionResult(this);
                }

                string userId = tokenParts[0];
                string emailToken = tokenParts[1];

                var result = await _authenticationService.ConfirmEmail(userId, emailToken);

                if (result == null || !result.IsSuccess)
                {
                    throw new Exception("Failed to confirm email");
                }

                return result.ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during email confirmation");

                return ResultWrapper.InternalServerError()
                    .ToActionResult(this);
            }
        }

        /// <summary>
        /// Resends the email confirmation link to a user
        /// </summary>
        /// <param name="request">The email to resend confirmation to</param>
        /// <returns>Result of the resend operation</returns>
        [HttpPost("resend-confirmation")]
        [IgnoreAntiforgeryToken]
        [EnableRateLimiting("AuthEndpoints")]
        [ProducesResponseType(typeof(BaseResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> ResendConfirmation([FromBody] ResendConfirmationRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return ResultWrapper.Failure(FailureReason.ValidationError,
                        "Invalid email",
                        "INVALID_REQUEST",
                        ModelState.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray())
                        )
                        .ToActionResult(this);
                }

                // Get client IP for rate limiting/security
                string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                _logger.LogInformation("Confirmation email resend attempt from IP {ClientIP}", clientIp);

                var result = await _authenticationService.ResendConfirmationEmailAsync(request.Email);

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during confirmation email resend");

                return ResultWrapper.InternalServerError()
                    .ToActionResult(this);
            }
        }

        [HttpPost("forgot-password")]
        [EnableRateLimiting("AuthEndpoints")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ResultWrapper.Failure(FailureReason.ValidationError,
                        "Invalid input",
                        "INVALID_REQUEST",
                        ModelState.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray())
                        )
                        .ToActionResult(this);
            }

            try
            {
                var result = await _authenticationService.ForgotPasswordAsync(request);

                return result.ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during forgot password email resend");

                return ResultWrapper.InternalServerError()
                    .ToActionResult(this);
            }
            
        }

        [HttpPost("reset-password")]
        [EnableRateLimiting("AuthEndpoints")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ResultWrapper.Failure(FailureReason.ValidationError,
                        "Invalid input",
                        "INVALID_REQUEST",
                        ModelState.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray())
                        )
                        .ToActionResult(this);
            }

            try
            {
                var result = await _authenticationService.ResetPasswordAsync(request);

                return result.ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during password reset");

                return ResultWrapper.InternalServerError()
                    .ToActionResult(this);
            }

        }

        /// <summary>
        /// Refreshes the access token using a refresh token
        /// </summary>
        /// <returns>New access and refresh tokens</returns>
        /// <response code="200">New tokens issued successfully</response>
        /// <response code="400">Invalid or missing tokens</response>
        /// <response code="401">Invalid or expired refresh token</response>
        [HttpPost]
        [Route("refresh-token")]
        [EnableRateLimiting("AuthEndpoints")]
        [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> RefreshToken(
            [FromServices] ICacheWarmupService cacheWarmup)
        {
            try
            {
                // Get the refresh token from cookie
                if (!Request.Cookies.TryGetValue("refreshToken", out string refreshToken))
                {
                    return ResultWrapper.Failure(FailureReason.ValidationError,
                        "Refresh token is missing",
                        "INVALID_REQUEST",
                        ModelState.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray())
                        )
                        .ToActionResult(this);
                }

                // Get the expired access token from the authorization header
                string accessToken = Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
                if (string.IsNullOrEmpty(accessToken))
                {
                    return ResultWrapper.Failure(FailureReason.ValidationError,
                        "Access token is missing",
                        "INVALID_REQUEST",
                        ModelState.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray())
                        )
                        .ToActionResult(this);
                }

                var result = await _authenticationService.RefreshToken(accessToken, refreshToken);

                if (!result.IsSuccess)
                {
                    // Clear refresh token cookie on failure
                    Response.Cookies.Delete("refreshToken");

                    return result.ToActionResult(this);
                }

                // Cast to LoginResponse to access the tokens
                var loginResponse = result.Data as LoginResponse;

                // ✅ Trigger cache warmup on token refresh too
                cacheWarmup.QueueUserCacheWarmup(Guid.Parse(loginResponse.UserId));

                // Set new refresh token in cookie
                SetRefreshTokenCookie(loginResponse.RefreshToken);

                // Return new access token in response body
                return result.ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during token refresh");

                return ResultWrapper.InternalServerError()
                    .ToActionResult(this);
            }
        }

        /// <summary>
        /// Adds a role to a user
        /// </summary>
        /// <param name="request">Role assignment request</param>
        /// <returns>Result of role assignment operation</returns>
        [HttpPost]
        [Route("add-role")]
        [Authorize(Roles = "ADMIN")]
        [EnableRateLimiting("AuthEndpoints")]
        [ProducesResponseType(typeof(BaseResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> AddRoleToUser([FromBody] AddRoleRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ResultWrapper.Failure(FailureReason.ValidationError,
                        "Invalid role assignment data",
                        "INVALID_REQUEST",
                        ModelState.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray())
                        )
                        .ToActionResult(this);
            }

            try
            {
                var result = await _authenticationService.AddRoleToUser(request);

                return result.ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during adding role");

                return ResultWrapper.InternalServerError()
                    .ToActionResult(this);
            }
        }

        /// <summary>
        /// Removes a role from a user
        /// </summary>
        /// <param name="request">Role removal request</param>
        /// <returns>Result of role removal operation</returns>
        [HttpPost]
        [Route("remove-role")]
        [Authorize(Roles = "ADMIN")]
        [EnableRateLimiting("AuthEndpoints")]
        [ProducesResponseType(typeof(BaseResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> RemoveRoleFromUser([FromBody] AddRoleRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ResultWrapper.Failure(FailureReason.ValidationError,
                        "Invalid role removal data",
                        "INVALID_REQUEST",
                        ModelState.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray())
                        )
                        .ToActionResult(this);
            }

            try
            {
                var result = await _authenticationService.RemoveRoleFromUser(request);

                return result.ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during removing role");

                return ResultWrapper.InternalServerError()
                    .ToActionResult(this);
            }
        }

        /// <summary>
        /// Logs out the current user by invalidating tokens
        /// </summary>
        /// <returns>Result of logout operation</returns>
        [HttpPost]
        [Route("logout")]
        [Authorize]
        [IgnoreAntiforgeryToken]
        [EnableRateLimiting("AuthEndpoints")]
        [ProducesResponseType(typeof(BaseResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Logout()
        {
            try
            {
                string userEmail = User.FindFirstValue(ClaimTypes.Email);

                // Clear refresh token cookie
                ClearRefreshTokenCookie();

                await HttpContext.SignOutAsync();

                _logger.LogInformation("User {UserId} logged out", userEmail);

                return ResultWrapper.Success("Logout successful")
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during logout");

                return ResultWrapper.InternalServerError()
                    .ToActionResult(this);
            }
        }

        #region Helper Methods

        private void SetRefreshTokenCookie(string refreshToken)
        {
            var cookieOptions = new CookieOptions
            {
                HttpOnly = true,
                Secure = true, // Requires HTTPS
                SameSite = SameSiteMode.Strict,
                Expires = DateTime.UtcNow.AddDays(7), // Match refresh token expiry in service
                Path = "/api/v1/auth/refresh-token" // Restrict cookie to token refresh endpoint
            };

            Response.Cookies.Append("refreshToken", refreshToken, cookieOptions);
        }

        private void ClearRefreshTokenCookie()
        {
            var cookieOptions = new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Path = "/api/v1/auth/refresh-token",
                Expires = DateTime.UtcNow.AddDays(-1) // Expire immediately
            };

            Response.Cookies.Append("refreshToken", "", cookieOptions);
        }

        #endregion
    }
}