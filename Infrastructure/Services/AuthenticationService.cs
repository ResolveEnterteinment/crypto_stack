using Application.Contracts.Requests.Auth;
using Application.Contracts.Responses;
using Application.Contracts.Responses.Auth;
using Application.Interfaces;
using Domain.DTOs;
using Domain.Models.Authentication;
using Domain.Models.User;
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
        //private readonly IEmailService _emailService;
        //private readonly ISystemSettingsService _systemSettingsService;

        public AuthenticationService(
            RoleManager<ApplicationRole> roleManager,
            ILogger<AuthenticationService> logger,
            UserManager<ApplicationUser> userManager,
            IOptionsSnapshot<JwtSettings> jwtSettings,
            IUserService userService
            //, IEmailService emailService
            )
        {
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _jwtSettings = jwtSettings.Value ?? throw new ArgumentNullException(nameof(jwtSettings));
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));
            _roleManager = roleManager ?? throw new ArgumentNullException(nameof(roleManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            //_emailService = emailService;
        }

        public async Task<LoginResponse> LoginAsync(LoginRequest request)
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
                    return new LoginResponse { EmailConfirmed = true, Message = "Invalid credentials", Success = false };
                }

                if (!user.EmailConfirmed)
                {
                    _logger.LogInformation("Login attempt for unconfirmed email: {EmailHash}", ComputeSHA256Hash(request.Email));
                    return new LoginResponse { EmailConfirmed = false, Message = "Email is not confirmed", Success = false };
                }

                // Check if user is locked out
                if (await _userManager.IsLockedOutAsync(user))
                {
                    _logger.LogWarning("Login attempt for locked out user: {EmailHash}", ComputeSHA256Hash(request.Email));
                    return new LoginResponse
                    {
                        EmailConfirmed = true,
                        Message = "Account is temporarily locked. Please try again later or reset your password.",
                        Success = false
                    };
                }

                bool isPasswordCorrect = await _userManager.CheckPasswordAsync(user, request.Password);

                if (!isPasswordCorrect)
                {
                    // Record failed login attempt for lockout
                    await _userManager.AccessFailedAsync(user);
                    _logger.LogWarning("Invalid password provided for user: {EmailHash}", ComputeSHA256Hash(request.Email));

                    return new LoginResponse { EmailConfirmed = true, Message = "Invalid credentials", Success = false };
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

                return new LoginResponse
                {
                    Username = user.Fullname,
                    AccessToken = new JwtSecurityTokenHandler().WriteToken(accessToken),
                    RefreshToken = refreshToken,
                    Message = "Login Successful",
                    UserId = user.Id.ToString(),
                    Success = true,
                    EmailConfirmed = true,
                };
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

                return new LoginResponse { EmailConfirmed = true, Success = false, Message = message };
            }
        }

        public async Task<RegisterResponse> RegisterAsync(RegisterRequest request)
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
                    return new RegisterResponse { Message = "Email already registered.", Success = false };
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
                        return new RegisterResponse { Message = errorMessage, Success = false };
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
                    return new RegisterResponse
                    {
                        Message = $"Registration failed: {errorMessage}",
                        Success = false
                    };
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

                // Send verification email (commented out as email service not implemented)
                /* await _emailService.SendEmailConfirmationMail(user, token);
                 _ = _emailService.SendNewUserMailToAdmin(user.Email, user.Id.ToString());
                 _ = _emailService.SendWelcomeMailToNewUser(user.Email, new {});*/

                _logger.LogInformation("User registered successfully: {UserId}", user.Id);

                return new RegisterResponse
                {
                    Success = true,
                    Message = "Registration successful! Please check your email to verify your account."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during user registration for email hash: {EmailHash}",
                    ComputeSHA256Hash(request.Email));

                return new RegisterResponse
                {
                    Success = false,
                    Message = "Registration failed due to a system error. Please try again later."
                };
            }
        }

        public async Task<BaseResponse> AddRoleToUser(AddRoleRequest request)
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
                    return new BaseResponse { Message = "User not found.", Success = false };
                }

                // Check if role exists
                ApplicationRole? role = await _roleManager.FindByNameAsync(request.Role);

                if (role is null)
                {
                    _logger.LogWarning("Attempted to add non-existent role {Role} to user {UserId}",
                        request.Role, request.UserId);
                    return new BaseResponse { Message = "Role not found.", Success = false };
                }

                // Check if user already has the role
                if (await _userManager.IsInRoleAsync(user, role.Name))
                {
                    _logger.LogInformation("User {UserId} already has role {Role}", request.UserId, request.Role);
                    return new BaseResponse { Success = true, Message = "User already has this role." };
                }

                var result = await _userManager.AddToRoleAsync(user, role.Name!);

                if (!result.Succeeded)
                {
                    string errorMessage = string.Join(", ", result.Errors.Select(e => e.Description));
                    _logger.LogError("Failed to add role {Role} to user {UserId}: {Errors}",
                        request.Role, request.UserId, errorMessage);
                    throw new InvalidOperationException($"Failed to add user '{user.UserName}' to role '{request.Role}': {errorMessage}");
                }

                _logger.LogInformation("Successfully added role {Role} to user {UserId}",
                    request.Role, request.UserId);
                return new BaseResponse { Success = true, Message = $"Role '{request.Role}' added successfully." };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding role {Role} to user {UserId}",
                    request.Role, request.UserId);

                return new BaseResponse(success: false, message: ex.Message);
            }
        }

        public async Task<BaseResponse> RemoveRoleFromUser(AddRoleRequest request)
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
                    return new BaseResponse { Message = "User not found.", Success = false };
                }

                // Check if role exists
                ApplicationRole? role = await _roleManager.FindByNameAsync(request.Role);

                if (role is null)
                {
                    _logger.LogWarning("Attempted to remove non-existent role {Role} from user {UserId}",
                        request.Role, request.UserId);
                    return new BaseResponse { Message = "Role not found.", Success = false };
                }

                // Check if user has the role before removing
                if (!await _userManager.IsInRoleAsync(user, role.Name))
                {
                    _logger.LogInformation("User {UserId} does not have role {Role}", request.UserId, request.Role);
                    return new BaseResponse { Success = true, Message = "User does not have this role." };
                }

                var result = await _userManager.RemoveFromRoleAsync(user, role.Name!);

                if (!result.Succeeded)
                {
                    string errorMessage = string.Join(", ", result.Errors.Select(e => e.Description));
                    _logger.LogError("Failed to remove role {Role} from user {UserId}: {Errors}",
                        request.Role, request.UserId, errorMessage);
                    throw new InvalidOperationException($"Failed to remove role '{request.Role}' from user '{user.UserName}': {errorMessage}");
                }

                _logger.LogInformation("Successfully removed role {Role} from user {UserId}",
                    request.Role, request.UserId);
                return new BaseResponse { Success = true, Message = $"Role '{request.Role}' removed successfully." };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing role {Role} from user {UserId}",
                    request.Role, request.UserId);

                return new BaseResponse(success: false, message: ex.Message);
            }
        }

        public async Task<BaseResponse> ConfirmEmail(string userId, string token)
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
                    throw new ApplicationException($"User with ID '{userId}' not found.");
                }

                // Validate token
                if (string.IsNullOrWhiteSpace(token))
                {
                    _logger.LogWarning("Empty email confirmation token for user {UserId}", userId);
                    throw new ApplicationException("Invalid confirmation token.");
                }

                var result = await _userManager.ConfirmEmailAsync(user, token);
                if (!result.Succeeded)
                {
                    string errorMessage = string.Join(", ", result.Errors.Select(e => e.Description));
                    _logger.LogWarning("Email confirmation failed for user {UserId}: {Errors}",
                        userId, errorMessage);
                    throw new ApplicationException($"Error confirming email: {errorMessage}");
                }

                _logger.LogInformation("Email confirmed successfully for user {UserId}", userId);
                return new BaseResponse
                {
                    Success = true,
                    Message = "/login?message=Your email address has been confirmed. You can now log in."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error confirming email for user {UserId}", userId);
                return new BaseResponse
                {
                    Success = false,
                    Message = $"Email confirmation failed: {ex.Message}"
                };
            }
        }

        public async Task<BaseResponse> RefreshToken(string accessToken, string refreshToken)
        {
            try
            {
                var principal = GetPrincipalFromExpiredToken(accessToken);
                if (principal == null)
                {
                    _logger.LogWarning("Invalid access token during refresh attempt");
                    return new BaseResponse { Success = false, Message = "Invalid access token" };
                }

                string userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
                var user = await _userManager.FindByIdAsync(userId);

                if (user == null || user.RefreshToken != refreshToken || user.RefreshTokenExpiryTime <= DateTime.UtcNow)
                {
                    _logger.LogWarning("Invalid refresh token for user {UserId}", userId);
                    return new BaseResponse { Success = false, Message = "Invalid or expired refresh token" };
                }

                var roles = await _userManager.GetRolesAsync(user);
                var newAccessToken = GenerateJwtToken(user, roles);
                var newRefreshToken = GenerateRefreshToken();

                // Update refresh token in database
                user.RefreshToken = newRefreshToken;
                user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpirationDays);
                await _userManager.UpdateAsync(user);

                _logger.LogInformation("Token refreshed successfully for user {UserId}", userId);

                return new LoginResponse
                {
                    Success = true,
                    Message = "Token refreshed successfully",
                    AccessToken = new JwtSecurityTokenHandler().WriteToken(newAccessToken),
                    RefreshToken = newRefreshToken,
                    UserId = user.Id.ToString(),
                    Username = user.Fullname,
                    EmailConfirmed = user.EmailConfirmed
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during token refresh");
                return new BaseResponse { Success = false, Message = "Error refreshing token" };
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
            // In a real implementation, this would get the IP from HttpContext
            // For now, return a placeholder
            return "IP_NOT_IMPLEMENTED";
        }

        #endregion
    }
}