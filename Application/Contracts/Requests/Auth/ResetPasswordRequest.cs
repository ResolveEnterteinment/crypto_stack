using System.ComponentModel.DataAnnotations;

namespace Application.Contracts.Requests.Auth
{
    public class ResetPasswordRequest
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Token { get; set; } = string.Empty;

        [Required, DataType(DataType.Password)]
        public string NewPassword { get; set; } = string.Empty;

        [Required, DataType(DataType.Password)]
        [Compare("NewPassword", ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}