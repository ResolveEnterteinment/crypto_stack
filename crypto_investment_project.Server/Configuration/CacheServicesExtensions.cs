using Application.Interfaces;

namespace crypto_investment_project.Server.Configuration
{
    public static class CacheServicesExtensions
    {
        public static IServiceCollection AddCacheServices(this IServiceCollection services)
        {
            // Register cache warmup service as singleton first
            services.AddSingleton<CacheWarmupService>();
            services.AddSingleton<ICacheWarmupService>(provider => provider.GetRequiredService<CacheWarmupService>());

            // Register as hosted service using the singleton
            services.AddHostedService(provider => provider.GetRequiredService<CacheWarmupService>());

            return services;
        }
    }
}