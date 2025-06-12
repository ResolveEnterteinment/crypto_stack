using System.ComponentModel.DataAnnotations;

namespace Application.Contracts.Requests.Auth
{
    public class CreateRoleRequest
    {
        [Required]
        public string Role { get; set; } = string.Empty;
    }
}
