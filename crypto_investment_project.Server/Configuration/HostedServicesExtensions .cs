using Infrastructure.Background;

namespace crypto_investment_project.Server.Configuration;

public static class HostedServicesExtensions
{
    public static IServiceCollection AddHostedServices(this IServiceCollection services, IWebHostEnvironment environment)
    {
        services.AddHostedService<SubscriptionRetryBackgroundService>();
        services.AddHostedService<OldPaymentCleanupBackgroundService>();

        return services;
    }
}