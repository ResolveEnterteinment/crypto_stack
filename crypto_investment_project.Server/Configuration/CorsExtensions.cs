namespace crypto_investment_project.Server.Configuration;

public static class CorsExtensions
{
    public static IServiceCollection ConfigureCorsPolicy(this IServiceCollection services, IWebHostEnvironment environment, IConfiguration configuration)
    {
        services.AddCors(options =>
        {
            options.AddPolicy("AllowSpecifiedOrigins", policy =>
            {
                // Production - fetch from configuration
                var allowedOrigins = configuration.GetSection("AllowedOrigins").Get<string[]>()
                    ?? Array.Empty<string>();

                policy.WithOrigins(allowedOrigins)
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials()
                        .WithExposedHeaders("X-CSRF-TOKEN");
            });
        });

        return services;
    }
}