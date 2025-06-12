using Application.Contracts.Requests.Auth;
using Application.Contracts.Responses;
using Application.Contracts.Responses.Auth;

namespace Application.Interfaces
{
    public interface IAuthenticationService
    {
        Task<LoginResponse> LoginAsync(LoginRequest request);
        Task<RegisterResponse> RegisterAsync(RegisterRequest request);
        Task<BaseResponse> AddRoleToUser(AddRoleRequest request);
        Task<BaseResponse> RemoveRoleFromUser(AddRoleRequest request);
        Task<BaseResponse> ConfirmEmail(string userId, string token);

        /// <summary>
        /// Sends a password reset email to the specified email address
        /// </summary>
        /// <param name="request">Email address to send the reset link to</param>
        /// <returns>Response indicating success or failure</returns>
        Task<BaseResponse> ForgotPasswordAsync(ForgotPasswordRequest request);

        /// <summary>
        /// Resets a user's password using the provided token
        /// </summary>
        /// <param name="request">Reset password details including token and new password</param>
        /// <returns>Response indicating success or failure</returns>
        Task<BaseResponse> ResetPasswordAsync(ResetPasswordRequest request);
        Task<BaseResponse> RefreshToken(string accessToken, string refreshToken);
        Task<bool> UserHasRole(string userId, string roleName);
        Task<BaseResponse> ResendConfirmationEmailAsync(string email);
    }
}
