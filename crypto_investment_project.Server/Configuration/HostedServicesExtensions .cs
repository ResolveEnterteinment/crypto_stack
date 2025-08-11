using Application.Interfaces;
using Infrastructure.Background;
using Infrastructure.Services.KYC;

namespace crypto_investment_project.Server.Configuration;

public static class HostedServicesExtensions
{
    public static IServiceCollection AddHostedServices(this IServiceCollection services, IWebHostEnvironment environment)
    {
        services.AddHostedService<SubscriptionRetryBackgroundService>();
        services.AddHostedService<OldPaymentCleanupBackgroundService>();
        services.AddHostedService(sp => sp.GetRequiredService<OpenSanctionsService>());

        // Change stream services
        services.AddHostedService<DashboardChangeStreamService>();

        return services;
    }
}