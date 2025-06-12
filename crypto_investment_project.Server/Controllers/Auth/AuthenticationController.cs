using Application.Contracts.Requests.Auth;
using Application.Contracts.Responses;
using Application.Contracts.Responses.Auth;
using Domain.DTOs.Error;
using Domain.Models.Authentication;
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
        private readonly ILogger<AuthenticationController> _logger;
        private readonly HtmlEncoder _htmlEncoder;

        public AuthenticationController(
            RoleManager<ApplicationRole> roleManager,
            Application.Interfaces.IAuthenticationService authenticationService,
            ILogger<AuthenticationController> logger,
            HtmlEncoder htmlEncoder)
        {
            _roleManager = roleManager ?? throw new ArgumentNullException(nameof(roleManager));
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
                    return BadRequest(new ErrorResponse
                    {
                        Message = "Invalid role data provided",
                        Code = "INVALID_ROLE_DATA",
                        ValidationErrors = ModelState.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray()),
                        TraceId = HttpContext.TraceIdentifier
                    });
                }

                // Sanitize role name for security
                string sanitizedRoleName = _htmlEncoder.Encode(request.Role.Trim());

                // Check if role already exists
                if (await _roleManager.RoleExistsAsync(sanitizedRoleName))
                {
                    return Conflict(new ErrorResponse
                    {
                        Message = $"Role '{sanitizedRoleName}' already exists",
                        Code = "ROLE_ALREADY_EXISTS",
                        TraceId = HttpContext.TraceIdentifier
                    });
                }

                var appRole = new ApplicationRole { Name = sanitizedRoleName };
                var createRoleResult = await _roleManager.CreateAsync(appRole);

                if (!createRoleResult.Succeeded)
                {
                    return StatusCode(500, new ErrorResponse
                    {
                        Message = "Failed to create role",
                        Code = "ROLE_CREATION_FAILED",
                        ValidationErrors = new Dictionary<string, string[]>
                        {
                            ["Errors"] = createRoleResult.Errors.Select(e => e.Description).ToArray()
                        },
                        TraceId = HttpContext.TraceIdentifier
                    });
                }

                _logger.LogInformation("Role {RoleName} created successfully by {UserId}",
                    sanitizedRoleName, User.FindFirstValue(ClaimTypes.NameIdentifier));

                return Ok(new BaseResponse
                {
                    Success = true,
                    Message = $"Role '{sanitizedRoleName}' created successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating role {RoleName}", request.Role);

                return StatusCode(500, new ErrorResponse
                {
                    Message = "An error occurred while creating the role",
                    Code = "INTERNAL_SERVER_ERROR",
                    TraceId = HttpContext.TraceIdentifier
                });
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
        [ProducesResponseType(typeof(RegisterResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new ErrorResponse
                    {
                        Message = "Invalid registration data",
                        Code = "INVALID_REGISTRATION_DATA",
                        ValidationErrors = ModelState.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray()),
                        TraceId = HttpContext.TraceIdentifier
                    });
                }

                // Get client IP for rate limiting/security
                string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                _logger.LogInformation("Registration attempt from IP {ClientIP}", clientIp);

                var result = await _authenticationService.RegisterAsync(request);

                // Even if registration fails due to duplicate email or other validation,
                // we return 200 with failure details to prevent user enumeration attacks
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during registration");

                return StatusCode(500, new ErrorResponse
                {
                    Message = "An error occurred during registration",
                    Code = "REGISTRATION_ERROR",
                    TraceId = HttpContext.TraceIdentifier
                });
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
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new ErrorResponse
                    {
                        Message = "Invalid login data",
                        Code = "INVALID_LOGIN_DATA",
                        ValidationErrors = ModelState.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray()),
                        TraceId = HttpContext.TraceIdentifier
                    });
                }

                // Get client IP for rate limiting/security
                string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                _logger.LogInformation("Login attempt from IP {ClientIP}", clientIp);

                var result = await _authenticationService.LoginAsync(request);

                if (!result.Success)
                {
                    // Return 401 for authentication failures
                    return Unauthorized(new ErrorResponse
                    {
                        Message = result.Message,
                        Code = "AUTHENTICATION_FAILED",
                        TraceId = HttpContext.TraceIdentifier
                    });
                }

                // Set refresh token in HTTP-only cookie for better security
                SetRefreshTokenCookie(result.RefreshToken);

                // Return success response with access token
                return Ok(new
                {
                    result.Success,
                    result.Message,
                    result.AccessToken,
                    result.UserId,
                    result.Username,
                    result.EmailConfirmed
                    // Note: RefreshToken deliberately not included in response body
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login");

                return StatusCode(500, new ErrorResponse
                {
                    Message = "An error occurred during login",
                    Code = "LOGIN_ERROR",
                    TraceId = HttpContext.TraceIdentifier
                });
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
                return BadRequest(new ErrorResponse
                {
                    Message = "Invalid confirmation token",
                    Code = "INVALID_TOKEN",
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            try
            {
                // Split the combined token to extract userId and the actual token
                string[] tokenParts = request.Token.Split(':', 2);
                if (tokenParts.Length != 2)
                {
                    return BadRequest(new ErrorResponse
                    {
                        Message = "Invalid token format",
                        Code = "INVALID_TOKEN_FORMAT",
                        TraceId = HttpContext.TraceIdentifier
                    });
                }

                string userId = tokenParts[0];
                string emailToken = tokenParts[1];

                var result = await _authenticationService.ConfirmEmail(userId, emailToken);

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during email confirmation");
                
                return StatusCode(500, new ErrorResponse
                {
                    Message = "An error occurred during email confirmation",
                    Code = "EMAIL_CONFIRMATION_ERROR",
                    TraceId = HttpContext.TraceIdentifier
                });
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
                    return BadRequest(new ErrorResponse
                    {
                        Message = "Invalid email",
                        Code = "INVALID_EMAIL",
                        ValidationErrors = ModelState.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray()),
                        TraceId = HttpContext.TraceIdentifier
                    });
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
                
                return StatusCode(500, new ErrorResponse
                {
                    Message = "An error occurred during confirmation email resend",
                    Code = "RESEND_CONFIRMATION_ERROR",
                    TraceId = HttpContext.TraceIdentifier
                });
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
        public async Task<IActionResult> RefreshToken()
        {
            try
            {
                // Get the refresh token from cookie
                if (!Request.Cookies.TryGetValue("refreshToken", out string refreshToken))
                {
                    return BadRequest(new ErrorResponse
                    {
                        Message = "Refresh token is missing",
                        Code = "MISSING_REFRESH_TOKEN",
                        TraceId = HttpContext.TraceIdentifier
                    });
                }

                // Get the expired access token from the authorization header
                string accessToken = Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
                if (string.IsNullOrEmpty(accessToken))
                {
                    return BadRequest(new ErrorResponse
                    {
                        Message = "Access token is missing",
                        Code = "MISSING_ACCESS_TOKEN",
                        TraceId = HttpContext.TraceIdentifier
                    });
                }

                var result = await _authenticationService.RefreshToken(accessToken, refreshToken);

                if (!result.Success)
                {
                    // Clear refresh token cookie on failure
                    Response.Cookies.Delete("refreshToken");

                    return Unauthorized(new ErrorResponse
                    {
                        Message = result.Message,
                        Code = "INVALID_REFRESH_TOKEN",
                        TraceId = HttpContext.TraceIdentifier
                    });
                }

                // Cast to LoginResponse to access the tokens
                var loginResponse = result as LoginResponse;

                // Set new refresh token in cookie
                SetRefreshTokenCookie(loginResponse.RefreshToken);

                // Return new access token in response body
                return Ok(new
                {
                    loginResponse.Success,
                    loginResponse.Message,
                    loginResponse.AccessToken,
                    loginResponse.UserId,
                    loginResponse.Username,
                    loginResponse.EmailConfirmed
                    // Note: RefreshToken deliberately not included in response body
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing token");

                return StatusCode(500, new ErrorResponse
                {
                    Message = "An error occurred while refreshing the token",
                    Code = "REFRESH_TOKEN_ERROR",
                    TraceId = HttpContext.TraceIdentifier
                });
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
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new ErrorResponse
                    {
                        Message = "Invalid role assignment data",
                        Code = "INVALID_ROLE_ASSIGNMENT",
                        ValidationErrors = ModelState.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray()),
                        TraceId = HttpContext.TraceIdentifier
                    });
                }

                var result = await _authenticationService.AddRoleToUser(request);

                if (!result.Success)
                {
                    if (result.Message.Contains("not found"))
                    {
                        return NotFound(new ErrorResponse
                        {
                            Message = result.Message,
                            Code = "RESOURCE_NOT_FOUND",
                            TraceId = HttpContext.TraceIdentifier
                        });
                    }

                    return BadRequest(new ErrorResponse
                    {
                        Message = result.Message,
                        Code = "ROLE_ASSIGNMENT_FAILED",
                        TraceId = HttpContext.TraceIdentifier
                    });
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding role to user");

                return StatusCode(500, new ErrorResponse
                {
                    Message = "An error occurred while assigning the role",
                    Code = "ROLE_ASSIGNMENT_ERROR",
                    TraceId = HttpContext.TraceIdentifier
                });
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
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new ErrorResponse
                    {
                        Message = "Invalid role removal data",
                        Code = "INVALID_ROLE_REMOVAL",
                        ValidationErrors = ModelState.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray()),
                        TraceId = HttpContext.TraceIdentifier
                    });
                }

                var result = await _authenticationService.RemoveRoleFromUser(request);

                if (!result.Success)
                {
                    if (result.Message.Contains("not found"))
                    {
                        return NotFound(new ErrorResponse
                        {
                            Message = result.Message,
                            Code = "RESOURCE_NOT_FOUND",
                            TraceId = HttpContext.TraceIdentifier
                        });
                    }

                    return BadRequest(new ErrorResponse
                    {
                        Message = result.Message,
                        Code = "ROLE_REMOVAL_FAILED",
                        TraceId = HttpContext.TraceIdentifier
                    });
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing role from user");

                return StatusCode(500, new ErrorResponse
                {
                    Message = "An error occurred while removing the role",
                    Code = "ROLE_REMOVAL_ERROR",
                    TraceId = HttpContext.TraceIdentifier
                });
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
                Response.Cookies.Delete("refreshToken");

                await HttpContext.SignOutAsync();

                _logger.LogInformation("User {UserId} logged out", userEmail);

                return Ok(new BaseResponse
                {
                    Success = true,
                    Message = "Logout successful"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during logout");

                return StatusCode(500, new ErrorResponse
                {
                    Message = "An error occurred during logout",
                    Code = "LOGOUT_ERROR",
                    TraceId = HttpContext.TraceIdentifier
                });
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

        #endregion
    }
}