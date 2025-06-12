using Application.Interfaces.KYC;
using Domain.DTOs.Settings;
using Infrastructure.Services.KYC;

namespace crypto_investment_project.Server.Configuration
{
    public static class KycServiceExtensions
    {
        public static IServiceCollection AddKycServices(this IServiceCollection services, IConfiguration configuration)
        {
            // Configure settings
            _ = services.Configure<KycSettings>(configuration.GetSection("KYC"));

            _ = services.AddSingleton<OpenSanctionsService>();

            _ = services.AddScoped<IKycService, KycService>();

            return services;
        }
    }
}