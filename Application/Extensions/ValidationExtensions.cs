using Application.Contracts.Requests.Asset;
using Application.Contracts.Requests.Payment;
using Application.Contracts.Requests.Subscription;
using Application.Validation;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Extensions
{
    /// <summary>
    /// Extension method to register all validators with DI
    /// </summary>
    public static class ValidationExtensions
    {
        public static IServiceCollection AddValidators(this IServiceCollection services)
        {
            // Register all validators
            services.AddScoped<IValidator<SubscriptionCreateRequest>, SubscriptionCreateRequestValidator>();
            services.AddScoped<IValidator<SubscriptionUpdateRequest>, SubscriptionUpdateRequestValidator>();
            services.AddScoped<IValidator<AssetCreateRequest>, AssetCreateRequestValidator>();
            services.AddScoped<IValidator<AssetUpdateRequest>, AssetUpdateRequestValidator>();
            services.AddScoped<IValidator<PaymentIntentRequest>, PaymentIntentRequestValidator>();
            services.AddScoped<IValidator<ChargeRequest>, ChargeRequestValidator>();
            services.AddScoped<IValidator<Contracts.Requests.Auth.LoginRequest>, LoginRequestValidator>();
            services.AddScoped<IValidator<Contracts.Requests.Auth.RegisterRequest>, RegisterRequestValidator>();

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
