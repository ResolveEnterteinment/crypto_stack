using Application.Contracts.Requests.Auth;
using Application.Contracts.Responses;
using Application.Contracts.Responses.Auth;
using Domain.DTOs;

namespace Application.Interfaces
{
    public interface IAuthenticationService
    {
        Task<ResultWrapper<LoginResponse>> LoginAsync(LoginRequest request);
        Task<ResultWrapper> RegisterAsync(RegisterRequest request);
        Task<ResultWrapper> AddRoleToUser(AddRoleRequest request);
        Task<ResultWrapper> RemoveRoleFromUser(AddRoleRequest request);
        Task<ResultWrapper> ConfirmEmail(string userId, string token);

        /// <summary>
        /// Sends a password reset email to the specified email address
        /// </summary>
        /// <param name="request">Email address to send the reset link to</param>
        /// <returns>Response indicating success or failure</returns>
        Task<ResultWrapper> ForgotPasswordAsync(ForgotPasswordRequest request);

        /// <summary>
        /// Resets a user's password using the provided token
        /// </summary>
        /// <param name="request">Reset password details including token and new password</param>
        /// <returns>Response indicating success or failure</returns>
        Task<ResultWrapper> ResetPasswordAsync(ResetPasswordRequest request);

        Task<ResultWrapper<LoginResponse>> RefreshToken(string accessToken, string refreshToken);
        Task<bool> UserHasRole(string userId, string roleName);
        Task<List<string>> GetUserRolesAsync(string userId);
        Task<ResultWrapper> ResendConfirmationEmailAsync(string email);
    }
}