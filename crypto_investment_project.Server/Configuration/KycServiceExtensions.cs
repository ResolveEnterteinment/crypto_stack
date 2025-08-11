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
            services.Configure<KycSettings>(configuration.GetSection("KYC"));

            services.AddSingleton<OpenSanctionsService>();

            services.AddScoped<IKycService, KycService>();
            services.AddScoped<IKycSessionService, KycSessionService>();
            services.AddScoped<IKycAuditService, KycAuditService>();
            services.AddScoped<IDocumentService, DocumentService>();
            services.AddScoped<ILiveCaptureService, LiveCaptureService>();
            //services.AddScoped<ICrudRepository<DocumentData>, Repository<DocumentData>>();
            //services.AddScoped<ICrudRepository<LiveCaptureData>, Repository<LiveCaptureData>>();

            services.AddKycMiddleware(configuration.GetSection("KYC"));

            return services;
        }
    }
}