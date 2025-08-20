namespace Infrastructure.Services.FlowEngine.Security
{
    public interface IIdentityService
    {
        Task<UserInfo?> GetUserAsync(string userId, CancellationToken cancellationToken);
        Task<bool> HasPermissionAsync(string userId, string permission, CancellationToken cancellationToken);
        Task<bool> HasRoleAsync(string userId, string role, CancellationToken cancellationToken);
        Task<IReadOnlyList<string>> GetUserRolesAsync(string userId, CancellationToken cancellationToken);
        Task<IReadOnlyList<string>> GetUserPermissionsAsync(string userId, CancellationToken cancellationToken);
    }
}
