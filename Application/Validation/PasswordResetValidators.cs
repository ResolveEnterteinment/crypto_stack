using Application.Contracts.Requests.Auth;
using FluentValidation;
using System.Text.RegularExpressions;

namespace Application.Validation
{
    /// <summary>
    /// Validator for ForgotPasswordRequest
    /// </summary>
    public class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
    {
        private static readonly Regex EmailRegex = new(
            @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$",
            RegexOptions.Compiled);

        public ForgotPasswordRequestValidator()
        {
            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Email is required")
                .EmailAddress().WithMessage("A valid email is required")
                .Must(email => EmailRegex.IsMatch(email)).WithMessage("Email format is invalid");
        }
    }

    /// <summary>
    /// Validator for ResetPasswordRequest
    /// </summary>
    public class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
    {
        private static readonly Regex EmailRegex = new(
            @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$",
            RegexOptions.Compiled);

        private static readonly Regex PasswordStrengthRegex = new(
            @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^\da-zA-Z]).{8,}$",
            RegexOptions.Compiled);

        public ResetPasswordRequestValidator()
        {
            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Email is required")
                .EmailAddress().WithMessage("A valid email is required")
                .Must(email => EmailRegex.IsMatch(email)).WithMessage("Email format is invalid");

            RuleFor(x => x.Token)
                .NotEmpty().WithMessage("Token is required");

            RuleFor(x => x.NewPassword)
                .NotEmpty().WithMessage("New password is required")
                .MinimumLength(8).WithMessage("Password must be at least 8 characters")
                .MaximumLength(100).WithMessage("Password cannot exceed 100 characters")
                .Must(password => PasswordStrengthRegex.IsMatch(password))
                .WithMessage("Password must contain at least one uppercase letter, one lowercase letter, one number, and one special character");

            RuleFor(x => x.ConfirmPassword)
                .NotEmpty().WithMessage("Password confirmation is required")
                .Equal(x => x.NewPassword).WithMessage("Passwords do not match");
        }
    }
}