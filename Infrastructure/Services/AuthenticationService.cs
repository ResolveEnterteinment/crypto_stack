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
using MongoDB.Bson;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Domain.Services
{
    public class AuthenticationService : IAuthenticationService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly JwtSettings _jwtSettings;
        private readonly IUserService _userService;
        private readonly RoleManager<ApplicationRole> _roleManager;
        private readonly ILogger _logger;
        //private readonly IEmailService _emailService;
        //private readonly ISystemSettingsService _systemSettingsService;

        public AuthenticationService(RoleManager<ApplicationRole> roleManager, ILogger<AuthenticationService> logger,
            UserManager<ApplicationUser> userManager, IOptionsSnapshot<JwtSettings> jwtSettings,
            IUserService userService/*, IEmailService emailService*/)
        {
            _userManager = userManager;
            _jwtSettings = jwtSettings.Value;
            _userService = userService;
            _roleManager = roleManager;
            _logger = logger;
            //_emailService = emailService;
        }

        public async Task<LoginResponse> LoginAsync(LoginRequest request)
        {
            try
            {
                var user = await _userManager.FindByEmailAsync(request.Email);

                if (user is null)
                {
                    return new LoginResponse { EmailConfirmed = true, Message = "Invalid email.", Success = false };
                }

                if (!user.EmailConfirmed)
                {
                    return new LoginResponse { EmailConfirmed = false, Message = "Email is not confirmed", Success = false };
                }

                bool isPasswordCorrect = await _userManager.CheckPasswordAsync(user, request.Password);

                if (!isPasswordCorrect)
                {
                    return new LoginResponse { EmailConfirmed = true, Message = "Invalid password.", Success = false };
                }

                var claims = new List<Claim>
                {
                    new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                    new Claim(ClaimTypes.Name, user.UserName!),
                    new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())
                };

                var roles = await _userManager.GetRolesAsync(user);
                var roleClaims = roles.Select(s => new Claim(ClaimTypes.Role, s));

                claims.AddRange(roleClaims);

                var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Key));

                var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

                var expires = DateTime.Now.AddDays(1);

                var token = new JwtSecurityToken(
                    issuer: _jwtSettings.Issuer,
                    audience: _jwtSettings.Audiance,
                    claims: claims,
                    expires: expires,
                    signingCredentials: creds
                    );

                var userData = await _userService.GetAsync(ObjectId.Parse(user.Id.ToString()));

                if (userData == null)
                {
                    throw new Exception("Can't find the userData");
                }

                return new LoginResponse
                {
                    Username = user.Fullname,
                    AccessToken = new JwtSecurityTokenHandler().WriteToken(token),
                    Message = "Login Successful",
                    UserId = user.Id.ToString(),
                    Success = true,
                    EmailConfirmed = true,
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);

                string message = "";

                if (ex.GetType() == typeof(TimeoutException))
                {
                    message = "Timeout occured. Please try again later";
                }

                return new LoginResponse { EmailConfirmed = true, Success = false, Message = message };
            }
        }

        public async Task<RegisterResponse> RegisterAsync(RegisterRequest request)
        {
            try
            {
                ApplicationUser? user = await _userManager.FindByEmailAsync(request.Email);

                if (user != null)
                {
                    return new RegisterResponse { Message = "Invalid email.", Success = false };
                }

                user = new ApplicationUser
                {
                    Fullname = request.FullName,
                    Email = request.Email,
                    UserName = request.Email,
                    ConcurrencyStamp = Guid.NewGuid().ToString(),
                };

                var createUserResult = await _userManager.CreateAsync(user, request.Password);

                if (!createUserResult.Succeeded)
                {
                    return new RegisterResponse { Message = $"Register user failed {createUserResult?.Errors?.First()?.Description}", Success = false };
                }

                // user created
                // add role
                var addUserToRoleResult = await _userManager.AddToRoleAsync(user, "USER");

                if (!addUserToRoleResult.Succeeded)
                {
                    return new RegisterResponse { Success = false, Message = $"Register User succeeded but cant add user to role {addUserToRoleResult?.Errors?.First()?.Description}" };
                }

                // success

                //var ss = await _systemSettingsService.GetSystemSettings();

                var userData = await _userService.CreateAsync(new UserData()
                {
                    _id = ObjectId.Parse(user.Id.ToString()),
                    CreateTime = DateTime.UtcNow,
                    FullName = request.FullName,
                    Email = request.Email,
                });

                if (userData == null)
                {
                    throw new Exception("Create UserData failed");
                }

                var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);

                // Send verification email
                /* await _emailService.SendEmailConfirmationMail(user, token);
                 _ = _emailService.SendNewUserMailToAdmin(user.Email, user.Id.ToString());
                 _ = _emailService.SendWelcomeMailToNewUser(user.Email, new
                 {

                 });*/

                return new RegisterResponse
                {
                    Success = true,
                    Message = "Register Successful"
                };

            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                return new RegisterResponse { Success = false, Message = ex.Message };

            }
        }

        public async Task<BaseResponse> AddRoleToUser(AddRoleRequest request)
        {
            try
            {
                var user = await _userManager.FindByIdAsync(request.UserId.ToString());

                if (user is null)
                {
                    return new BaseResponse { Message = "Can't find the user.", Success = false };
                }

                ApplicationRole? role = await _roleManager.FindByNameAsync(request.Role);

                if (role is null)
                {
                    return new BaseResponse { Message = "Can't find the role.", Success = false };
                }

                var result = await _userManager.AddToRoleAsync(user, role.Name!);

                if (!result.Succeeded)
                {
                    throw new InvalidOperationException($"Failed to add user '{user.UserName}' to role '{request.Role}'.");
                }

                return new BaseResponse { Success = true, Message = "" };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);

                return new BaseResponse(success: false, message: ex.Message);
            }
        }

        public async Task<BaseResponse> RemoveRoleFromUser(AddRoleRequest request)
        {
            try
            {
                var user = await _userManager.FindByIdAsync(request.UserId.ToString());

                if (user is null)
                {
                    return new BaseResponse { Message = "Can't find the user.", Success = false };
                }

                ApplicationRole? role = await _roleManager.FindByNameAsync(request.Role);

                if (role is null)
                {
                    return new BaseResponse { Message = "Can't find the role.", Success = false };
                }

                var result = await _userManager.RemoveFromRoleAsync(user, role.Name!);

                if (!result.Succeeded)
                {
                    throw new InvalidOperationException($"Failed to add user '{user.UserName}' to role '{request.Role}'.");
                }

                return new BaseResponse { Success = true, Message = "" };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);

                return new BaseResponse(success: false, message: ex.Message);
            }
        }

        public async Task<BaseResponse> ConfirmEmail(string userId, string token)
        {
            try
            {
                var user = await _userManager.FindByIdAsync(userId);
                if (user == null)
                {
                    throw new ApplicationException($"User with ID '{userId}' not found.");
                }

                var result = await _userManager.ConfirmEmailAsync(user, token);
                if (!result.Succeeded)
                {
                    throw new ApplicationException($"Error confirming email for user with ID '{userId}'.");
                }

                //var ss = await _systemSettingsService.GetSystemSettings();

                return new BaseResponse { Success = true, Message = "/login?message=Your email address has been confirmed." };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                return new RegisterResponse { Success = false, Message = ex.Message };
            }
        }

        public async Task<bool> UserHasRole(string userId, string roleName)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                // User not found
                return false;
            }

            return await _userManager.IsInRoleAsync(user, roleName);
        }

    }

}
