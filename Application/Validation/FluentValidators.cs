using Application.Contracts.Requests.Asset;
using Application.Contracts.Requests.Payment;
using Application.Contracts.Requests.Subscription;
using Domain.Constants;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Application.Validation
{
    public static class FluentValidators
    {
        /// <summary>
        /// Registers all FluentValidation validators in the specified assemblies
        /// </summary>
        /// <param name="services">The service collection</param>
        /// <param name="assemblies">The assemblies to scan for validators</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection AddFluentValidators(
            this IServiceCollection services,
            params Assembly[] assemblies)
        {
            // If no assemblies specified, use the calling assembly
            if (assemblies == null || assemblies.Length == 0)
            {
                assemblies = new[] { Assembly.GetCallingAssembly() };
            }

            // Register all validators from the specified assemblies
            foreach (var assembly in assemblies)
            {
                // Find all types that implement IValidator<T>
                var validatorTypes = assembly.GetTypes()
                    .Where(t => !t.IsAbstract && !t.IsInterface)
                    .Where(t => t.GetInterfaces()
                        .Any(i => i.IsGenericType &&
                             i.GetGenericTypeDefinition() == typeof(IValidator<>)))
                    .ToList();

                foreach (var validatorType in validatorTypes)
                {
                    // Get the type being validated (T in IValidator<T>)
                    var entityType = validatorType.GetInterfaces()
                        .First(i => i.IsGenericType &&
                               i.GetGenericTypeDefinition() == typeof(IValidator<>))
                        .GetGenericArguments()[0];

                    // Register the validator
                    var validatorInterface = typeof(IValidator<>).MakeGenericType(entityType);
                    services.AddTransient(validatorInterface, validatorType);
                }
            }

            return services;
        }

        /// <summary>
        /// Validates an object using FluentValidation and throws ValidationException if validation fails
        /// </summary>
        /// <typeparam name="T">The type of object to validate</typeparam>
        /// <param name="validator">The validator instance</param>
        /// <param name="instance">The object to validate</param>
        /// <returns>A task representing the asynchronous validation</returns>
        public static async Task ValidateAndThrowAsync<T>(this IValidator<T> validator, T instance)
        {
            var validationResult = await validator.ValidateAsync(instance);

            if (!validationResult.IsValid)
            {
                var errors = validationResult.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(e => e.ErrorMessage).ToArray()
                    );

                throw new Domain.Exceptions.ValidationException($"Validation failed for {typeof(T).Name}", errors);
            }
        }
    }

    /// <summary>
    /// Validator for SubscriptionCreateRequest
    /// </summary>
    public class SubscriptionCreateRequestValidator : AbstractValidator<SubscriptionCreateRequest>
    {
        public SubscriptionCreateRequestValidator()
        {
            RuleFor(x => x.UserId)
                .NotEmpty().WithMessage("UserId is required")
                .Must(id => Guid.TryParse(id, out _)).WithMessage("UserId must be a valid Guid");

            RuleFor(x => x.Allocations)
                .NotEmpty().WithMessage("At least one allocation is required")
                .Must(allocations => allocations.Any()).WithMessage("At least one allocation is required");

            RuleForEach(x => x.Allocations).SetValidator(new AllocationValidator());

            RuleFor(x => x.Amount)
                .GreaterThan(0).WithMessage("Amount must be greater than zero")
                .LessThan(100000).WithMessage("Amount cannot exceed 100,000"); // Add reasonable upper limit

            RuleFor(x => x.Interval)
                .NotEmpty().WithMessage("Interval is required")
                .Must(interval => SubscriptionInterval.AllValues.Contains(interval))
                .WithMessage($"Interval must be one of: {string.Join(", ", SubscriptionInterval.AllValues)}");

            RuleFor(x => x.Currency)
                .NotEmpty().WithMessage("Currency is required")
                .Length(3).WithMessage("Currency code must be 3 characters")
                .Must(BeValidCurrencyCode).WithMessage("Currency must be a valid ISO currency code");

            RuleFor(x => x.Allocations)
                .Must(HaveTotalPercentOf100)
                .WithMessage("Allocation percentages must total exactly 100%");

            // Add a custom rule for EndDate if present
            When(x => x.EndDate.HasValue, () =>
            {
                RuleFor(x => x.EndDate.Value)
                    .GreaterThan(DateTime.UtcNow).WithMessage("End date must be in the future");
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

        private bool HaveTotalPercentOf100(IEnumerable<AllocationRequest> allocations)
        {
            if (allocations == null || !allocations.Any())
                return false;

            var total = allocations.Sum(a => a.PercentAmount);
            return total == 100; // Exact match required
        }
    }

    /// <summary>
    /// Validator for SubscriptionUpdateRequest
    /// </summary>
    public class SubscriptionUpdateRequestValidator : AbstractValidator<SubscriptionUpdateRequest>
    {
        public SubscriptionUpdateRequestValidator()
        {
            // Only validate allocations if provided
            When(x => x.Allocations != null && x.Allocations.Any(), () =>
            {
                RuleForEach(x => x.Allocations).SetValidator(new AllocationValidator());

                RuleFor(x => x.Allocations)
                    .Must(HaveTotalPercentOf100)
                    .WithMessage("Allocation percentages must total exactly 100%");
            });

            When(x => x.Amount.HasValue, () =>
            {
                RuleFor(x => x.Amount)
                    .GreaterThan(0).WithMessage("Amount must be greater than zero")
                    .LessThan(100000).WithMessage("Amount cannot exceed 100,000");
            });

            When(x => x.EndDate.HasValue, () =>
            {
                RuleFor(x => x.EndDate.Value)
                    .GreaterThan(DateTime.UtcNow).WithMessage("End date must be in the future");
            });
        }

        private bool HaveTotalPercentOf100(IEnumerable<AllocationRequest> allocations)
        {
            if (allocations == null || !allocations.Any())
                return false;

            var total = allocations.Sum(a => a.PercentAmount);
            return total == 100; // Exact match required
        }
    }

    /// <summary>
    /// Validator for AssetCreateRequest
    /// </summary>
    public class AssetCreateRequestValidator : AbstractValidator<AssetCreateRequest>
    {
        public AssetCreateRequestValidator()
        {

        }
    }

    /// <summary>
    /// Validator for AssetUpdateRequest
    /// </summary>
    public class AssetUpdateRequestValidator : AbstractValidator<AssetUpdateRequest>
    {
        public AssetUpdateRequestValidator()
        {
        }
    }

    /// <summary>
    /// Validator for AllocationRequest
    /// </summary>
    public class AllocationValidator : AbstractValidator<AllocationRequest>
    {
        public AllocationValidator()
        {
            RuleFor(x => x.AssetId)
                .NotEmpty().WithMessage("AssetId is required")
                .Must(id => Guid.TryParse(id, out _)).WithMessage("AssetId must be a valid Guid");

            RuleFor(x => x.PercentAmount)
                .GreaterThan(0).WithMessage("PercentAmount must be greater than zero")
                .LessThanOrEqualTo(100).WithMessage("PercentAmount cannot exceed 100");
        }
    }

    /// <summary>
    /// Validator for PaymentIntentRequest
    /// </summary>
    public class PaymentIntentRequestValidator : AbstractValidator<PaymentIntentRequest>
    {
        public PaymentIntentRequestValidator()
        {
            RuleFor(x => x.UserId)
                .NotEmpty().WithMessage("UserId is required")
                .Must(id => Guid.TryParse(id, out _)).WithMessage("UserId must be a valid Guid");

            RuleFor(x => x.SubscriptionId)
                .NotEmpty().WithMessage("SubscriptionId is required")
                .Must(id => Guid.TryParse(id, out _)).WithMessage("SubscriptionId must be a valid Guid");

            RuleFor(x => x.Provider)
                .NotEmpty().WithMessage("Provider is required")
                .MaximumLength(50).WithMessage("Provider name cannot exceed 50 characters");

            RuleFor(x => x.PaymentId)
                .NotEmpty().WithMessage("PaymentId is required")
                .MaximumLength(100).WithMessage("PaymentId cannot exceed 100 characters");

            RuleFor(x => x.Amount)
                .GreaterThan(0).WithMessage("TotalAmount must be greater than zero")
                .LessThan(1000000).WithMessage("TotalAmount exceeds maximum allowed value");

            /*RuleFor(x => x.PaymentProviderFee)
                .GreaterThanOrEqualTo(0).WithMessage("PaymentProviderFee cannot be negative")
                .LessThan(x => x.Amount).WithMessage("Fee cannot be greater than total amount");

            RuleFor(x => x.PlatformFee)
                .GreaterThanOrEqualTo(0).WithMessage("PlatformFee cannot be negative")
                .LessThan(x => x.Amount).WithMessage("Fee cannot be greater than total amount");

            RuleFor(x => x.NetAmount)
                .GreaterThan(0).WithMessage("NetAmount must be greater than zero")
                .LessThanOrEqualTo(x => x.Amount).WithMessage("NetAmount cannot exceed TotalAmount")
                .Must((request, netAmount) =>
                    Math.Abs(netAmount - (request.Amount - request.PaymentProviderFee - request.PlatformFee)) < 0.01m)
                .WithMessage("NetAmount must equal TotalAmount minus fees");*/

            RuleFor(x => x.Currency)
                .NotEmpty().WithMessage("Currency is required")
                .Length(3).WithMessage("Currency code must be 3 characters");

            RuleFor(x => x.Status)
                .NotEmpty().WithMessage("Status is required")
                .MaximumLength(50).WithMessage("Status cannot exceed 50 characters");
        }
    }

    /// <summary>
    /// Validator for ChargeRequest
    /// </summary>
    public class ChargeRequestValidator : AbstractValidator<ChargeRequest>
    {
        public ChargeRequestValidator()
        {
            RuleFor(x => x.Id)
                .NotEmpty().WithMessage("Charge Id is required")
                .MaximumLength(100).WithMessage("Charge Id cannot exceed 100 characters");

            RuleFor(x => x.PaymentIntentId)
                .NotEmpty().WithMessage("PaymentIntentId is required")
                .MaximumLength(100).WithMessage("PaymentIntentId cannot exceed 100 characters");

            RuleFor(x => x.Amount)
                .GreaterThan(0).WithMessage("Amount must be greater than zero");

            RuleFor(x => x.Currency)
                .NotEmpty().WithMessage("Currency is required")
                .Length(3).WithMessage("Currency code must be 3 characters");
        }
    }

    /// <summary>
    /// Validator for LoginRequest
    /// </summary>
    public class LoginRequestValidator : AbstractValidator<Contracts.Requests.Auth.LoginRequest>
    {
        private static readonly Regex EmailRegex = new(
            @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$",
            RegexOptions.Compiled);

        public LoginRequestValidator()
        {
            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Email is required")
                .EmailAddress().WithMessage("A valid email is required")
                .Must(email => EmailRegex.IsMatch(email)).WithMessage("Email format is invalid");

            RuleFor(x => x.Password)
                .NotEmpty().WithMessage("Password is required")
                .MinimumLength(8).WithMessage("Password must be at least 8 characters");
        }
    }

    /// <summary>
    /// Validator for RegisterRequest
    /// </summary>
    public class RegisterRequestValidator : AbstractValidator<Contracts.Requests.Auth.RegisterRequest>
    {
        private static readonly Regex EmailRegex = new(
            @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$",
            RegexOptions.Compiled);

        private static readonly Regex PasswordStrengthRegex = new(
            @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^\da-zA-Z]).{8,}$",
            RegexOptions.Compiled);

        public RegisterRequestValidator()
        {
            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Email is required")
                .EmailAddress().WithMessage("A valid email is required")
                .Must(email => EmailRegex.IsMatch(email)).WithMessage("Email format is invalid");

            RuleFor(x => x.FullName)
                .NotEmpty().WithMessage("Full name is required")
                .MinimumLength(2).WithMessage("Full name must be at least 2 characters")
                .MaximumLength(100).WithMessage("Full name cannot exceed 100 characters");

            RuleFor(x => x.Password)
                .NotEmpty().WithMessage("Password is required")
                .MinimumLength(8).WithMessage("Password must be at least 8 characters")
                .MaximumLength(100).WithMessage("Password cannot exceed 100 characters")
                .Must(password => PasswordStrengthRegex.IsMatch(password))
                .WithMessage("Password must contain at least one uppercase letter, one lowercase letter, one number, and one special character");
        }
    }

    /// <summary>
    /// Validator for ResendConfirmationRequest
    /// </summary>
    public class ResendConfirmationRequestValidator : AbstractValidator<Contracts.Requests.Auth.ResendConfirmationRequest>
    {
        private static readonly Regex EmailRegex = new(
            @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$",
            RegexOptions.Compiled);

        public ResendConfirmationRequestValidator()
        {
            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Email is required")
                .EmailAddress().WithMessage("A valid email is required")
                .Must(email => EmailRegex.IsMatch(email)).WithMessage("Email format is invalid");
        }
    }
}