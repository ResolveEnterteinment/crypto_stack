namespace crypto_investment_project.Server.Configuration;

public static class CorsExtensions
{
    public static IServiceCollection ConfigureCorsPolicy(this IServiceCollection services, IWebHostEnvironment environment, IConfiguration configuration)
    {
        services.AddCors(options =>
        {
            options.AddPolicy("AllowSpecifiedOrigins", policy =>
            {
                // In development, allow all local origins
                if (environment.IsDevelopment())
                {
                    policy.SetIsOriginAllowed(origin => new Uri(origin).Host == "localhost")
                          .AllowAnyHeader()
                          .AllowAnyMethod()
                          .AllowCredentials() // Required for CSRF cookies
                          .WithExposedHeaders("X-CSRF-TOKEN"); // Expose CSRF token header
                }
                else
                {
                    // Production - fetch from configuration
                    var allowedOrigins = configuration.GetSection("AllowedOrigins").Get<string[]>()
                        ?? Array.Empty<string>();

                    policy.WithOrigins(allowedOrigins)
                          .AllowAnyHeader()
                          .AllowAnyMethod()
                          .AllowCredentials()
                          .WithExposedHeaders("X-CSRF-TOKEN");
                }
            });
        });

        return services;
    }
}