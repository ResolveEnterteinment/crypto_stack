using System.ComponentModel.DataAnnotations;

namespace Application.Contracts.Requests.Auth
{
    public class ForgotPasswordRequest
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;
    }
}