using Application.Contracts.Requests.Auth;
using Application.Contracts.Responses.Auth;
using Application.Interfaces;
using Domain.Constants;
using Domain.DTOs;
using Domain.Models.Authentication;
using Domain.Models.User;
using Infrastructure.Services.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Infrastructure.Services
{
    public class AuthenticationService : IAuthenticationService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly JwtSettings _jwtSettings;
        private readonly IUserService _userService;
        private readonly RoleManager<ApplicationRole> _roleManager;
        private readonly ILogger<AuthenticationService> _logger;
        private readonly IEmailService _emailService;
        private readonly IHttpContextService _httpContextService;

        public AuthenticationService(
            RoleManager<ApplicationRole> roleManager,
            ILogger<AuthenticationService> logger,
            UserManager<ApplicationUser> userManager,
            IOptionsSnapshot<JwtSettings> jwtSettings,
            IUserService userService,
            IEmailService emailService,
            IHttpContextService httpContextService)
        {
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _jwtSettings = jwtSettings.Value ?? throw new ArgumentNullException(nameof(jwtSettings));
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));
            _roleManager = roleManager ?? throw new ArgumentNullException(nameof(roleManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
            _httpContextService = httpContextService ?? throw new ArgumentNullException(nameof(httpContextService));
        }

        public async Task<ResultWrapper<LoginResponse>> LoginAsync(LoginRequest request)
        {
            try
            {
                // Add tracing for security auditing
                using var logScope = _logger.BeginScope(new Dictionary<string, object>
                {
                    ["EmailHash"] = ComputeSHA256Hash(request.Email),
                    ["IPAddress"] = GetUserIPAddress(), // Implement or use HttpContext from DI
                    ["Action"] = "Login"
                });

                var user = await _userManager.FindByEmailAsync(request.Email);

                if (user is null)
                {
                    _logger.LogWarning("Login attempt with invalid email");
                    return ResultWrapper<LoginResponse>.Failure(Domain.Constants.FailureReason.ValidationError, "Invalid credentials", errorCode: "INVALID_CREDENTIALS");
                }

                if (!user.EmailConfirmed)
                {
                    _logger.LogInformation("Login attempt for unconfirmed email: {EmailHash}", ComputeSHA256Hash(request.Email));
                    return ResultWrapper<LoginResponse>.Failure(FailureReason.ValidationError, "Email is not confirmed", errorCode: "EMAIL_NOT_CONFIRMED");
                }

                // Check if user is locked out
                if (await _userManager.IsLockedOutAsync(user))
                {
                    _logger.LogWarning("Login attempt for locked out user: {EmailHash}", ComputeSHA256Hash(request.Email));

                    return ResultWrapper<LoginResponse>.Failure(FailureReason.ValidationError, "Account is temporarily locked. Please try again later or reset your password.", errorCode: "ACCOUNT_LOCKED");
                }

                bool isPasswordCorrect = await _userManager.CheckPasswordAsync(user, request.Password);

                if (!isPasswordCorrect)
                {
                    // Record failed login attempt for lockout
                    await _userManager.AccessFailedAsync(user);
                    _logger.LogWarning("Invalid password provided for user: {EmailHash}", ComputeSHA256Hash(request.Email));

                    return ResultWrapper<LoginResponse>.Failure(FailureReason.ValidationError, "Invalid credentials", errorCode: "INVALID_CREDENTIALS");
                }

                // Reset failed login attempts on successful login
                await _userManager.ResetAccessFailedCountAsync(user);

                // Generate tokens
                var roles = await _userManager.GetRolesAsync(user);
                var accessToken = GenerateJwtToken(user, roles);
                var refreshToken = GenerateRefreshToken();

                // Save refresh token to user
                user.RefreshToken = refreshToken;
                user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpirationDays);
                await _userManager.UpdateAsync(user);

                var userData = await _userService.GetAsync(user.Id);

                if (userData == null)
                {
                    _logger.LogError("User data not found for authenticated user ID: {UserId}", user.Id);
                    throw new InvalidOperationException("User data not found");
                }

                _logger.LogInformation("User successfully logged in: {UserId}", user.Id);

                return ResultWrapper<LoginResponse>.Success(new LoginResponse
                {
                    Username = user.Fullname,
                    AccessToken = new JwtSecurityTokenHandler().WriteToken(accessToken),
                    RefreshToken = refreshToken,
                    UserId = user.Id.ToString(),
                    EmailConfirmed = true,
                }, "Login Successful");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during user login for email hash: {EmailHash}",
                    ComputeSHA256Hash(request.Email));

                string message = ex switch
                {
                    TimeoutException => "Authentication service unavailable. Please try again later.",
                    InvalidOperationException ex1 when ex1.Message.Contains("User data") =>
                        "User profile is incomplete. Please contact support.",
                    _ => "An error occurred while processing your request. Please try again later."
                };

                return ResultWrapper<LoginResponse>.Failure(FailureReason.ValidationError, message, errorCode: "UNKNOWN_ERROR");
            }
        }

        public async Task<ResultWrapper> RegisterAsync(RegisterRequest request)
        {
            try
            {
                // Add tracing for security auditing
                using var logScope = _logger.BeginScope(new Dictionary<string, object>
                {
                    ["EmailHash"] = ComputeSHA256Hash(request.Email),
                    ["IPAddress"] = GetUserIPAddress(), // Implement or use HttpContext from DI
                    ["Action"] = "Register"
                });

                // Check if email already exists
                ApplicationUser? existingUser = await _userManager.FindByEmailAsync(request.Email);

                if (existingUser != null)
                {
                    _logger.LogWarning("Registration attempt with existing email: {EmailHash}",
                        ComputeSHA256Hash(request.Email));
                    return ResultWrapper.Failure(FailureReason.ValidationError, "Email already registered", errorCode: "EMAIL_ALREADY_REGISTERED");
                }

                // Validate password strength
                var passwordValidators = _userManager.PasswordValidators;
                foreach (var validator in passwordValidators)
                {
                    var result = await validator.ValidateAsync(_userManager, null, request.Password);
                    if (!result.Succeeded)
                    {
                        string errorMessage = string.Join(", ", result.Errors.Select(e => e.Description));
                        _logger.LogWarning("Password validation failed: {Errors}", errorMessage);
                        return ResultWrapper.Failure(FailureReason.ValidationError, $"Password validation failed: {errorMessage}", errorCode: "PASSWORD_VALIDATION_FAILED");
                    }
                }

                // Create user
                var user = new ApplicationUser
                {
                    Fullname = request.FullName,
                    Email = request.Email,
                    UserName = request.Email,
                    SecurityStamp = Guid.NewGuid().ToString(),
                    ConcurrencyStamp = Guid.NewGuid().ToString(),
                    EmailConfirmed = false // Require email verification
                };

                var createUserResult = await _userManager.CreateAsync(user, request.Password);

                if (!createUserResult.Succeeded)
                {
                    string errorMessage = string.Join(", ", createUserResult.Errors.Select(e => e.Description));
                    _logger.LogWarning("User creation failed: {Errors}", errorMessage);

                    return ResultWrapper.Failure(FailureReason.ValidationError, $"User registration failed: {errorMessage}", errorCode: "USER_REGISTRATION_FAILED");
                }

                // Add user to USER role
                var addUserToRoleResult = await _userManager.AddToRoleAsync(user, "USER");

                if (!addUserToRoleResult.Succeeded)
                {
                    string errorMessage = string.Join(", ", addUserToRoleResult.Errors.Select(e => e.Description));
                    _logger.LogError("Failed to add user to role: {Errors}", errorMessage);

                    // Still proceed with account creation, but log the error
                    // This is a non-fatal issue that can be fixed by admins
                }

                // Create user profile data
                var userData = await _userService.CreateAsync(new UserData()
                {
                    Id = user.Id,
                    FullName = request.FullName,
                    Email = request.Email,
                });

                if (userData == null)
                {
                    _logger.LogError("Failed to create user data profile for new user: {UserId}", user.Id);
                    // Don't fail registration, but log the error
                }

                // Generate email verification token
                var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);

                // Send verification email
                await _emailService.SendEmailConfirmationMailAsync(user, token);

                // Send welcome and admin notifications
                _ = _emailService.SendWelcomeMailToNewUserAsync(user.Email, new { name = user.Fullname });
                _ = _emailService.SendNewUserMailToAdminAsync(user.Email, user.Id.ToString());

                _logger.LogInformation("User registered successfully: {UserId}", user.Id);

                return ResultWrapper.Success("Registration successful! Please check your email to verify your account.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during user registration for email hash: {EmailHash}",
                    ComputeSHA256Hash(request.Email));

                return ResultWrapper.Failure(FailureReason.ValidationError, "An error occurred while processing your registration. Please try again later.", errorCode: "REGISTRATION_ERROR");
            }
        }

        public async Task<ResultWrapper> AddRoleToUser(AddRoleRequest request)
        {
            try
            {
                using var logScope = _logger.BeginScope(new Dictionary<string, object>
                {
                    ["UserId"] = request.UserId,
                    ["Role"] = request.Role,
                    ["Action"] = "AddRole"
                });

                var user = await _userManager.FindByIdAsync(request.UserId.ToString());

                if (user is null)
                {
                    _logger.LogWarning("Attempted to add role {Role} to non-existent user {UserId}",
                        request.Role, request.UserId);
                    return ResultWrapper.Failure(FailureReason.NotFound, "User not found.", errorCode: "USER_NOT_FOUND");
                }

                // Check if role exists
                ApplicationRole? role = await _roleManager.FindByNameAsync(request.Role);

                if (role is null)
                {
                    _logger.LogWarning("Attempted to add non-existent role {Role} to user {UserId}",
                        request.Role, request.UserId);
                    return ResultWrapper.Failure(FailureReason.NotFound, "Role not found.", errorCode: "ROLE_NOT_FOUND");
                }

                // Check if user already has the role
                if (await _userManager.IsInRoleAsync(user, role.Name))
                {
                    _logger.LogInformation("User {UserId} already has role {Role}", request.UserId, request.Role);
                    return ResultWrapper.Success("User already has this role.");
                }

                var result = await _userManager.AddToRoleAsync(user, role.Name!);

                if (!result.Succeeded)
                {
                    string errorMessage = string.Join(", ", result.Errors.Select(e => e.Description));
                    _logger.LogError("Failed to add role {Role} to user {UserId}: {Errors}",
                        request.Role, request.UserId, errorMessage);
                    return ResultWrapper.Failure(FailureReason.ValidationError, $"Failed to add user '{user.UserName}' to role '{request.Role}': {errorMessage}", errorCode: "ROLE_ASSIGNMENT_FAILED");
                }

                _logger.LogInformation("Successfully added role {Role} to user {UserId}",
                    request.Role, request.UserId);
                return ResultWrapper.Success($"Role '{request.Role}' added successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding role {Role} to user {UserId}",
                    request.Role, request.UserId);

                string message = ex switch
                {
                    TimeoutException => "Authentication service unavailable. Please try again later.",
                    _ => "An error occurred while processing your request. Please try again later."
                };

                return ResultWrapper.Failure(FailureReason.Unknown, message, errorCode: "UNKNOWN_ERROR");
            }
        }

        public async Task<ResultWrapper> RemoveRoleFromUser(AddRoleRequest request)
        {
            try
            {
                using var logScope = _logger.BeginScope(new Dictionary<string, object>
                {
                    ["UserId"] = request.UserId,
                    ["Role"] = request.Role,
                    ["Action"] = "RemoveRole"
                });

                var user = await _userManager.FindByIdAsync(request.UserId.ToString());

                if (user is null)
                {
                    _logger.LogWarning("Attempted to remove role {Role} from non-existent user {UserId}",
                        request.Role, request.UserId);
                    return ResultWrapper.Failure(FailureReason.NotFound, "User not found.", errorCode: "USER_NOT_FOUND");
                }

                // Check if role exists
                ApplicationRole? role = await _roleManager.FindByNameAsync(request.Role);

                if (role is null)
                {
                    _logger.LogWarning("Attempted to remove non-existent role {Role} from user {UserId}",
                        request.Role, request.UserId);
                    return ResultWrapper.Failure(FailureReason.NotFound, "Role not found.", errorCode: "ROLE_NOT_FOUND");
                }

                // Check if user has the role before removing
                if (!await _userManager.IsInRoleAsync(user, role.Name))
                {
                    _logger.LogInformation("User {UserId} does not have role {Role}", request.UserId, request.Role);
                    return ResultWrapper.Success("User does not have this role.");
                }

                var result = await _userManager.RemoveFromRoleAsync(user, role.Name!);

                if (!result.Succeeded)
                {
                    string errorMessage = string.Join(", ", result.Errors.Select(e => e.Description));
                    _logger.LogError("Failed to remove role {Role} from user {UserId}: {Errors}",
                        request.Role, request.UserId, errorMessage);
                    return ResultWrapper.Failure(FailureReason.ValidationError, $"Failed to remove role '{request.Role}' from user '{user.UserName}': {errorMessage}", errorCode: "ROLE_REMOVAL_FAILED");
                }

                _logger.LogInformation("Successfully removed role {Role} from user {UserId}",
                    request.Role, request.UserId);
                return ResultWrapper.Success($"Role '{request.Role}' removed successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing role {Role} from user {UserId}",
                    request.Role, request.UserId);

                string message = ex switch
                {
                    TimeoutException => "Authentication service unavailable. Please try again later.",
                    _ => "An error occurred while processing your request. Please try again later."
                };

                return ResultWrapper.Failure(FailureReason.Unknown, message, errorCode: "UNKNOWN_ERROR");
            }
        }

        public async Task<ResultWrapper> ConfirmEmail(string userId, string token)
        {
            try
            {
                using var logScope = _logger.BeginScope(new Dictionary<string, object>
                {
                    ["UserId"] = userId,
                    ["Action"] = "ConfirmEmail"
                });

                var user = await _userManager.FindByIdAsync(userId);
                if (user == null)
                {
                    _logger.LogWarning("Email confirmation attempt for non-existent user {UserId}", userId);
                    return ResultWrapper.Failure(FailureReason.NotFound, "Invalid confirmation link.", errorCode: "INVALID_TOKEN");
                }

                // Validate token
                if (string.IsNullOrWhiteSpace(token))
                {
                    _logger.LogWarning("Empty email confirmation token for user {UserId}", userId);
                    return ResultWrapper.Failure(FailureReason.ValidationError, "Invalid confirmation token.", errorCode: "INVALID_TOKEN");
                }

                var result = await _userManager.ConfirmEmailAsync(user, token);
                if (!result.Succeeded)
                {
                    string errorMessage = string.Join(", ", result.Errors.Select(e => e.Description));
                    _logger.LogWarning("Email confirmation failed for user {UserId}: {Errors}",
                        userId, errorMessage);

                    return ResultWrapper.Failure(FailureReason.ValidationError, $"Error confirming email: {errorMessage}", errorCode: "EMAIL_CONFIRMATION_FAILED");
                }

                _logger.LogInformation("Email confirmed successfully for user {UserId}", userId);
                return ResultWrapper.Success("Your email has been confirmed successfully. You can now log in.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error confirming email for user {UserId}", userId);
                string message = ex switch
                {
                    TimeoutException => "Authentication service unavailable. Please try again later.",
                    _ => "An error occurred while processing your request. Please try again later."
                };

                return ResultWrapper.Failure(FailureReason.Unknown, message, errorCode: "UNKNOWN_ERROR");
            }
        }

        public async Task<ResultWrapper<LoginResponse>> RefreshToken(string accessToken, string refreshToken)
        {
            try
            {
                var principal = GetPrincipalFromExpiredToken(accessToken);
                if (principal == null)
                {
                    _logger.LogWarning("Invalid access token during refresh attempt");
                    return ResultWrapper<LoginResponse>.Failure(FailureReason.ValidationError, "Invalid access token", errorCode: "INVALID_ACCESS_TOKEN");
                }

                string userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
                var user = await _userManager.FindByIdAsync(userId);

                if (user == null || user.RefreshToken != refreshToken || user.RefreshTokenExpiryTime <= DateTime.UtcNow)
                {
                    _logger.LogWarning("Invalid refresh token for user {UserId}", userId);
                    return ResultWrapper<LoginResponse>.Failure(FailureReason.ValidationError, "Invalid or expired refresh token", errorCode: "INVALID_REFRESH_TOKEN");
                }

                var roles = await _userManager.GetRolesAsync(user);
                var newAccessToken = GenerateJwtToken(user, roles);
                var newRefreshToken = GenerateRefreshToken();

                // Update refresh token in database
                user.RefreshToken = newRefreshToken;
                user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpirationDays);
                await _userManager.UpdateAsync(user);

                _logger.LogInformation("Token refreshed successfully for user {UserId}", userId);

                return ResultWrapper<LoginResponse>.Success(new LoginResponse
                {
                    AccessToken = new JwtSecurityTokenHandler().WriteToken(newAccessToken),
                    RefreshToken = newRefreshToken,
                    UserId = user.Id.ToString(),
                    Username = user.Fullname,
                    EmailConfirmed = user.EmailConfirmed
                }, "Token refreshed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during token refresh");
                string message = ex switch
                {
                    TimeoutException => "Authentication service unavailable. Please try again later.",
                    _ => "An error occurred while processing your request. Please try again later."
                };

                return ResultWrapper<LoginResponse>.Failure(FailureReason.Unknown, message, errorCode: "UNKNOWN_ERROR");
            }
        }

        public async Task<ResultWrapper> ResendConfirmationEmailAsync(string email)
        {
            try
            {
                // Add tracing for security auditing
                using var logScope = _logger.BeginScope(new Dictionary<string, object>
                {
                    ["EmailHash"] = ComputeSHA256Hash(email),
                    ["IPAddress"] = GetUserIPAddress(),
                    ["Action"] = "ResendConfirmation"
                });

                // Find the user by email
                var user = await _userManager.FindByEmailAsync(email);

                if (user == null)
                {
                    // For security reasons, don't reveal if the email exists or not
                    _logger.LogWarning("Confirmation email resend attempt for non-existent email: {EmailHash}",
                        ComputeSHA256Hash(email));

                    return ResultWrapper.Success("If your email exists in our system and is not yet confirmed, a confirmation link has been sent.");
                }

                // Check if email is already confirmed
                if (user.EmailConfirmed)
                {
                    _logger.LogInformation("Confirmation email resend attempt for already confirmed email: {EmailHash}",
                        ComputeSHA256Hash(email));

                    return ResultWrapper.Success("Your email is already confirmed. You can log in with your credentials.");
                }

                // Generate a new confirmation token
                var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);

                // Send the confirmation email
                await _emailService.SendEmailConfirmationMailAsync(user, token);

                _logger.LogInformation("Confirmation email resent successfully for user: {UserId}", user.Id);

                return ResultWrapper.Success("A new confirmation email has been sent. Please check your inbox.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during confirmation email resend for email hash: {EmailHash}",
                    ComputeSHA256Hash(email));

                string message = ex switch
                {
                    TimeoutException => "Authentication service unavailable. Please try again later.",
                    _ => "An error occurred while processing your request. Please try again later."
                };

                return ResultWrapper.Failure(FailureReason.Unknown, message, errorCode: "UNKNOWN_ERROR");
            }
        }

        public async Task<ResultWrapper> ForgotPasswordAsync(ForgotPasswordRequest request)
        {
            try
            {
                // Add tracing for security auditing
                using var logScope = _logger.BeginScope(new Dictionary<string, object>
                {
                    ["EmailHash"] = ComputeSHA256Hash(request.Email),
                    ["IPAddress"] = GetUserIPAddress(),
                    ["Action"] = "ForgotPassword"
                });

                // Find the user by email
                var user = await _userManager.FindByEmailAsync(request.Email);

                if (user == null)
                {
                    // For security reasons, don't reveal if the email exists or not
                    _logger.LogWarning("Password reset requested for non-existent email: {EmailHash}",
                        ComputeSHA256Hash(request.Email));

                    return ResultWrapper.Success("If your email exists in our system, you will receive a password reset link shortly.");
                }

                // Check if the email is confirmed
                if (!user.EmailConfirmed)
                {
                    _logger.LogInformation("Password reset requested for unconfirmed email: {EmailHash}",
                        ComputeSHA256Hash(request.Email));

                    return ResultWrapper.Failure(FailureReason.ValidationError, "Your email is not confirmed. Please check your inbox for the confirmation link or request a new one.", errorCode: "EMAIL_NOT_CONFIRMED");
                }

                // Generate password reset token
                var token = await _userManager.GeneratePasswordResetTokenAsync(user);

                // Send the reset email
                await _emailService.SendPasswordResetMailAsync(user, token);

                _logger.LogInformation("Password reset email sent successfully for user: {UserId}", user.Id);

                return ResultWrapper.Success("Password reset instructions have been sent to your email.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during password reset request for email hash: {EmailHash}",
                    ComputeSHA256Hash(request.Email));

                string message = ex switch
                {
                    TimeoutException => "Authentication service unavailable. Please try again later.",
                    _ => "An error occurred while processing your request. Please try again later."
                };

                return ResultWrapper.Failure(FailureReason.Unknown, message, errorCode: "UNKNOWN_ERROR");
            }
        }

        public async Task<ResultWrapper> ResetPasswordAsync(ResetPasswordRequest request)
        {
            try
            {
                // Add tracing for security auditing
                using var logScope = _logger.BeginScope(new Dictionary<string, object>
                {
                    ["EmailHash"] = ComputeSHA256Hash(request.Email),
                    ["IPAddress"] = GetUserIPAddress(),
                    ["Action"] = "ResetPassword"
                });

                // Find the user by email
                var user = await _userManager.FindByEmailAsync(request.Email);

                if (user == null)
                {
                    _logger.LogWarning("Password reset attempted for non-existent email: {EmailHash}",
                        ComputeSHA256Hash(request.Email));

                    return ResultWrapper.Failure(FailureReason.NotFound, "Invalid request. Please request a new password reset link.", errorCode: "PASSWORD_RESET_FAILED");
                }

                // Reset the password
                var result = await _userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);

                if (!result.Succeeded)
                {
                    string errorMessage = string.Join(", ", result.Errors.Select(e => e.Description));
                    _logger.LogWarning("Password reset failed for user {UserId}: {Errors}",
                        user.Id, errorMessage);

                    return ResultWrapper.Failure(FailureReason.ValidationError, $"Password reset failed: {errorMessage}", errorCode: "PASSWORD_RESET_FAILED");
                }

                // Reset failed login attempts on successful password reset
                await _userManager.ResetAccessFailedCountAsync(user);

                _logger.LogInformation("Password reset successfully for user {UserId}", user.Id);

                return ResultWrapper.Success("Your password has been reset successfully. You can now log in with your new password.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during password reset for email hash: {EmailHash}",
                    ComputeSHA256Hash(request.Email));

                string message = ex switch
                {
                    TimeoutException => "Authentication service unavailable. Please try again later.",
                    _ => "An error occurred while processing your request. Please try again later."
                };

                return ResultWrapper.Failure(FailureReason.Unknown, message, errorCode: "UNKNOWN_ERROR");
            }
        }

        public async Task<bool> UserHasRole(string userId, string roleName)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                _logger.LogWarning("Role check for non-existent user {UserId}", userId);
                return false;
            }

            return await _userManager.IsInRoleAsync(user, roleName);
        }

        #region Helper Methods

        private JwtSecurityToken GenerateJwtToken(ApplicationUser user, IList<string> roles)
        {
            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.UserName!),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
            };

            // Add role claims
            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            // Add custom claims if needed
            if (!string.IsNullOrEmpty(user.Fullname))
            {
                claims.Add(new Claim("fullName", user.Fullname));
            }

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Key));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var expires = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationMinutes);

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = expires,
                SigningCredentials = creds,
                Issuer = _jwtSettings.Issuer,
                Audience = _jwtSettings.Audience,
                NotBefore = DateTime.UtcNow // Token is valid starting now
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var tokenObject = tokenHandler.CreateJwtSecurityToken(tokenDescriptor);

            return tokenObject;
        }

        private string GenerateRefreshToken()
        {
            var randomNumber = new byte[64];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randomNumber);
            return Convert.ToBase64String(randomNumber);
        }

        private ClaimsPrincipal? GetPrincipalFromExpiredToken(string token)
        {
            var tokenValidationParameters = new TokenValidationParameters
            {
                ValidateAudience = true,
                ValidateIssuer = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Key)),
                ValidIssuer = _jwtSettings.Issuer,
                ValidAudience = _jwtSettings.Audience,
                ValidateLifetime = false // Important to allow expired tokens
            };

            var tokenHandler = new JwtSecurityTokenHandler();

            try
            {
                var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out SecurityToken securityToken);

                if (securityToken is not JwtSecurityToken jwtSecurityToken ||
                    !jwtSecurityToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
                {
                    return null;
                }

                return principal;
            }
            catch
            {
                return null;
            }
        }

        private string ComputeSHA256Hash(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = sha256.ComputeHash(bytes);

            return Convert.ToHexString(hash);
        }

        private string GetUserIPAddress()
        {
            return _httpContextService.GetClientIpAddress() ?? "Unknown IP";
        }

        #endregion
    }
}