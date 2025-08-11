using Infrastructure.Services.Http;

namespace crypto_investment_project.Server.Configuration
{
    public static class HttpContextExtensions
    {
        public static IServiceCollection AddHttpContextServices(this IServiceCollection services)
        {
            // Add HTTP context services
            services.AddHttpContextAccessor();
            services.AddScoped<IHttpContextService, HttpContextService>();

            return services;
        }
    }
}