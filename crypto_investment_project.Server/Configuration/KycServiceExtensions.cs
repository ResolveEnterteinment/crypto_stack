// crypto_investment_project.Server/Configuration/KycServiceExtensions.cs
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
            services.Configure<OnfidoSettings>(configuration.GetSection("KYC:Onfido"));
            services.Configure<SumSubSettings>(configuration.GetSection("KYC:SumSub"));
            services.Configure<KycServiceSettings>(configuration.GetSection("KYC:Settings"));

            // Register HttpClient factories for KYC providers
            services.AddHttpClient<OnfidoKycProvider>();
            services.AddHttpClient<SumSubKycProvider>();

            // Register KYC providers
            services.AddScoped<OnfidoKycProvider>();
            services.AddScoped<SumSubKycProvider>();

            // Register KYC services
            services.AddScoped<OnfidoKycService>();
            services.AddScoped<SumSubKycService>();

            // Register factory
            services.AddScoped<IKycServiceFactory, KycServiceFactory>();

            // Register main KYC service (factory-based)
            services.AddScoped<IKycService>(sp =>
            {
                var factory = sp.GetRequiredService<IKycServiceFactory>();
                return factory.GetKycService();
            });

            return services;
        }
    }
}