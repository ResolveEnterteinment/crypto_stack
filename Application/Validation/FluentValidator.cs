using Application.Contracts.Requests.Payment;
using Application.Contracts.Requests.Subscription;
using Domain.Constants;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Validation
{
    /// <summary>
    /// Validator for SubscriptionCreateRequest
    /// </summary>
    public class FluentValidator : AbstractValidator<SubscriptionCreateRequest>
    {
        public FluentValidator()
        {
            RuleFor(x => x.UserId)
                .NotEmpty().WithMessage("UserId is required")
                .Must(id => Guid.TryParse(id, out _)).WithMessage("UserId must be a valid Guid");

            RuleFor(x => x.Allocations)
                .NotEmpty().WithMessage("At least one allocation is required");

            RuleForEach(x => x.Allocations).SetValidator(new AllocationValidator());

            RuleFor(x => x.Amount)
                .GreaterThan(0).WithMessage("Amount must be greater than zero");

            RuleFor(x => x.Interval)
                .NotEmpty().WithMessage("Interval is required")
                .Must(interval => SubscriptionInterval.AllValues.Contains(interval))
                .WithMessage($"Interval must be one of: {string.Join(", ", SubscriptionInterval.AllValues)}");

            RuleFor(x => x.Currency)
                .NotEmpty().WithMessage("Currency is required");

            RuleFor(x => x.Allocations)
                .Must(HaveTotalPercentOf100)
                .WithMessage("Allocation percentages must total exactly 100%");
        }

        private bool HaveTotalPercentOf100(IEnumerable<AllocationRequest> allocations)
        {
            if (allocations == null || !allocations.Any())
                return false;

            var total = allocations.Sum(a => a.PercentAmount);
            return Math.Abs(total - 100) < 0.01m; // Allow for small floating point differences
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
    /// Validator for SubscriptionUpdateRequest
    /// </summary>
    public class SubscriptionUpdateRequestValidator : AbstractValidator<SubscriptionUpdateRequest>
    {
        public SubscriptionUpdateRequestValidator()
        {
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
                    .GreaterThan(0).WithMessage("Amount must be greater than zero");
            });

            When(x => !string.IsNullOrEmpty(x.Interval), () =>
            {
                RuleFor(x => x.Interval)
                    .Must(interval => SubscriptionInterval.AllValues.Contains(interval))
                    .WithMessage($"Interval must be one of: {string.Join(", ", SubscriptionInterval.AllValues)}");
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
            return Math.Abs(total - 100) < 0.01m; // Allow for small floating point differences
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
                .NotEmpty().WithMessage("Provider is required");

            RuleFor(x => x.PaymentId)
                .NotEmpty().WithMessage("PaymentId is required");

            RuleFor(x => x.TotalAmount)
                .GreaterThan(0).WithMessage("TotalAmount must be greater than zero");

            RuleFor(x => x.NetAmount)
                .GreaterThan(0).WithMessage("NetAmount must be greater than zero");

            RuleFor(x => x.Currency)
                .NotEmpty().WithMessage("Currency is required");
        }
    }

    /// <summary>
    /// Extension method to register all validators with DI
    /// </summary>
    public static class ValidationExtensions
    {
        public static IServiceCollection AddValidators(this IServiceCollection services)
        {
            services.AddScoped<IValidator<SubscriptionCreateRequest>, FluentValidator>();
            services.AddScoped<IValidator<SubscriptionUpdateRequest>, SubscriptionUpdateRequestValidator>();
            services.AddScoped<IValidator<Application.Contracts.Requests.Payment.PaymentIntentRequest>, PaymentIntentRequestValidator>();

            return services;
        }

        /// <summary>
        /// Validates an object and throws ValidationException if validation fails
        /// </summary>
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
}