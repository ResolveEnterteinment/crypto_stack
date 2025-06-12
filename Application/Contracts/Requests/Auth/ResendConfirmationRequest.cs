using System.ComponentModel.DataAnnotations;

namespace Application.Contracts.Requests.Auth
{
    public class ResendConfirmationRequest
    {
        [Required, EmailAddress]
        public string Email { get; set; } = null!;
    }
}