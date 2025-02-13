using Application.Contracts.Requests;
using System.ComponentModel.DataAnnotations;

namespace Application.Contracts.Requests.Auth
{
    public class LoginRequest 
    {
        [Required, EmailAddress]
        public string Email { get; set; } = null!;
        [Required, DataType(DataType.Password)]
        public string Password { get; set; } = null!;
    }
}
