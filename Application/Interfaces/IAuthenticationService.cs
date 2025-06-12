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
        public Task<BaseResponse> RefreshToken(string accessToken, string refreshToken);
        Task<bool> UserHasRole(string userId, string roleName);

        /// <summary>
        /// Resends the confirmation email to a user
        /// </summary>
        /// <param name="email">Email address of the user</param>
        /// <returns>Response indicating success or failure</returns>
        Task<BaseResponse> ResendConfirmationEmailAsync(string email);
    }
}
