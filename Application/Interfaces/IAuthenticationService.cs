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
        Task<bool> UserHasRole(string userId, string roleName);
    }
}
