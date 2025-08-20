using Domain.Constants;
using Domain.DTOs.Payment;
using FluentValidation;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Application.Validation
{
    /// <summary>
    /// Validator for CheckoutSessionRequest
    /// </summary>
    public class CheckoutSessionRequestValidator : AbstractValidator<CheckoutSessionRequest>
    {
        private static readonly Regex IdempotencyKeyRegex = new(
            @"^[a-zA-Z0-9\-_]+$",
            RegexOptions.Compiled);

        public CheckoutSessionRequestValidator()
        {
            RuleFor(x => x.SubscriptionId)
                .NotEmpty().WithMessage("SubscriptionId is required")
                .Must(id => Guid.TryParse(id, out _)).WithMessage("SubscriptionId must be a valid Guid");

            RuleFor(x => x.UserId)
                .NotEmpty().WithMessage("UserId is required")
                .Must(id => Guid.TryParse(id, out _)).WithMessage("UserId must be a valid Guid");

            RuleFor(x => x.Amount)
                .GreaterThan(0).WithMessage("Amount must be greater than zero")
                .LessThan(1000000).WithMessage("Amount exceeds maximum allowed value");

            RuleFor(x => x.Currency)
                .NotEmpty().WithMessage("Currency is required")
                .Length(3).WithMessage("Currency code must be 3 characters")
                .Must(BeValidCurrencyCode).WithMessage("Currency must be a valid ISO currency code");

            // Only validate interval if IsRecurring is true
            When(x => x.IsRecurring, () =>
            {
                RuleFor(x => x.Interval)
                    .NotEmpty().WithMessage("Interval is required when IsRecurring is true")
                    .Must(interval => SubscriptionInterval.AllValues.Contains(interval))
                    .WithMessage($"Interval must be one of: {string.Join(", ", SubscriptionInterval.AllValues)}");
            });

            // Optional URL validations
            When(x => !string.IsNullOrEmpty(x.ReturnUrl), () =>
            {
                RuleFor(x => x.ReturnUrl)
                    .Must(BeValidUrl).WithMessage("ReturnUrl must be a valid URL");
            });

            When(x => !string.IsNullOrEmpty(x.CancelUrl), () =>
            {
                RuleFor(x => x.CancelUrl)
                    .Must(BeValidUrl).WithMessage("CancelUrl must be a valid URL");
            });

            // IdempotencyKey validation (optional, but if provided should be valid)
            When(x => !string.IsNullOrEmpty(x.IdempotencyKey), () =>
            {
                RuleFor(x => x.IdempotencyKey)
                    .MaximumLength(255).WithMessage("IdempotencyKey cannot exceed 255 characters")
                    .Must(key => IdempotencyKeyRegex.IsMatch(key))
                    .WithMessage("IdempotencyKey can only contain alphanumeric characters, hyphens, and underscores");
            });
        }

        private bool BeValidCurrencyCode(string currencyCode)
        {
            try
            {
                // Check if it's a valid ISO currency code
                return !string.IsNullOrEmpty(currencyCode) &&
                       currencyCode.Length == 3 &&
                       CultureInfo.GetCultures(CultureTypes.SpecificCultures)
                           .Select(c => new RegionInfo(c.LCID).ISOCurrencySymbol)
                           .Distinct()
                           .Contains(currencyCode);
            }
            catch
            {
                // If RegionInfo creation fails, fall back to a simple check
                return !string.IsNullOrEmpty(currencyCode) &&
                       currencyCode.Length == 3 &&
                       currencyCode.All(char.IsLetter);
            }
        }

        private bool BeValidUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) && 
                   (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }
    }
}