using Infrastructure.Background;
using Infrastructure.Services.KYC;

namespace crypto_investment_project.Server.Configuration;

public static class HostedServicesExtensions
{
    public static IServiceCollection AddHostedServices(this IServiceCollection services, IWebHostEnvironment environment)
    {
        _ = services.AddHostedService<SubscriptionRetryBackgroundService>();
        _ = services.AddHostedService<OldPaymentCleanupBackgroundService>();
        _ = services.AddHostedService(sp => sp.GetRequiredService<OpenSanctionsService>());

        return services;
    }
}