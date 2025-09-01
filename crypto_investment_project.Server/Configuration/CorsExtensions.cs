namespace crypto_investment_project.Server.Configuration;

public static class CorsExtensions
{
    public static IServiceCollection ConfigureCorsPolicy(this IServiceCollection services, IConfiguration configuration)
    {
        // Production - fetch from configuration
        var allowedOrigins = configuration.GetSection("AllowedOrigins").Get<string[]>()
            ?? Array.Empty<string>();

        services.AddCors(options =>
        {
            options.AddPolicy("AllowSpecifiedOrigins", policy =>
            {
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