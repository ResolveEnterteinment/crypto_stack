using Infrastructure.HealthChecks;
using MongoDB.Driver;

namespace crypto_investment_project.Server.Configuration;

public static class HealthChecksExtensions
{
    public static IServiceCollection AddHealthChecksServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHealthChecks()
            .AddCheck<ExchangeApiHealthCheck>("exchange_api", tags: new[] { "readiness" })
            .AddMongoDb(
                clientFactory: sp => new MongoClient(configuration["MongoDB:ConnectionString"]),
                name: "mongodb",
                tags: new[] { "readiness" },
                timeout: TimeSpan.FromSeconds(3));

        return services;
    }
}