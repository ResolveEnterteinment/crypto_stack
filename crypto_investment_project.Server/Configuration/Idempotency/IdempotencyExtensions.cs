using crypto_investment_project.Server.Middleware;
using Domain.Settings;
using Microsoft.Extensions.Caching.Memory;

namespace crypto_investment_project.Server.Configuration.Idempotency
{
    /// <summary>
    /// Extension methods for configuring idempotency middleware and services
    /// </summary>
    public static class IdempotencyExtensions
    {
        /// <summary>
        /// Add idempotency middleware to the application pipeline
        /// </summary>
        public static IApplicationBuilder UseIdempotency(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<IdempotencyMiddleware>();
        }

        /// <summary>
        /// Add idempotency middleware and related services to the service collection
        /// </summary>
        public static IServiceCollection AddIdempotencyMiddleware(this IServiceCollection services, IConfiguration configuration)
        {
            // Register settings
            services.AddIdempotencySettings(configuration);

            // Ensure memory cache is registered
            if (!services.Any(x => x.ServiceType == typeof(IMemoryCache)))
            {
                services.AddMemoryCache();
            }

            return services;
        }

        /// <summary>
        /// Add idempotency settings to the service collection
        /// </summary>
        public static IServiceCollection AddIdempotencySettings(this IServiceCollection services, IConfiguration configuration)
        {
            var settings = configuration.GetSection(IdempotencySettings.SectionName).Get<IdempotencySettings>()
                ?? new IdempotencySettings();

            // Validate settings on startup
            settings.Validate();

            services.Configure<IdempotencySettings>(configuration.GetSection(IdempotencySettings.SectionName));
            services.AddSingleton(settings);

            return services;
        }

        /// <summary>
        /// Get idempotency settings from configuration
        /// </summary>
        public static IdempotencySettings GetIdempotencySettings(this IConfiguration configuration)
        {
            var settings = configuration.GetSection(IdempotencySettings.SectionName).Get<IdempotencySettings>()
                ?? new IdempotencySettings();
            settings.Validate();
            return settings;
        }
    }
}