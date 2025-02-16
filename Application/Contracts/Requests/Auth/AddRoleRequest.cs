namespace Application.Contracts.Requests.Auth
{
    public class AddRoleRequest
    {
        public Guid UserId { get; set; }
        public required string Role { get; set; }
    }
}
