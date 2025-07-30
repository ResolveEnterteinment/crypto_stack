using Application.Interfaces.Base;
using Application.Interfaces.KYC;
using Domain.DTOs.KYC;
using Domain.DTOs.Settings;
using Domain.Models.KYC;
using Infrastructure.Services.Base;
using Infrastructure.Services.KYC;

namespace crypto_investment_project.Server.Configuration
{
    public static class KycServiceExtensions
    {
        public static IServiceCollection AddKycServices(this IServiceCollection services, IConfiguration configuration)
        {
            // Configure settings
            services.Configure<KycSettings>(configuration.GetSection("KYC"));

            services.AddSingleton<OpenSanctionsService>();

            services.AddScoped<IKycService, KycService>();
            services.AddScoped<IDocumentService, DocumentService>();
            services.AddScoped<ICrudRepository<DocumentRecord>, Repository<DocumentRecord>>();
            services.AddScoped<ICrudRepository<LiveCaptureRecord>, Repository<LiveCaptureRecord>>();

            services.AddKycMiddleware(configuration.GetSection("KYC"));

            return services;
        }
    }
}