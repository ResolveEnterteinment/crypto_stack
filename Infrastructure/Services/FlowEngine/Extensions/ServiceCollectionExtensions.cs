using Infrastructure.Services.FlowEngine.Configuration.Options;
using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Core.Interfaces;
using Infrastructure.Services.FlowEngine.Engine;
using Infrastructure.Services.FlowEngine.Middleware;
using Infrastructure.Services.FlowEngine.Services.Events;
using Infrastructure.Services.FlowEngine.Services.Metrics;
using Infrastructure.Services.FlowEngine.Services.PauseResume;
using Infrastructure.Services.FlowEngine.Services.Persistence;
using Infrastructure.Services.FlowEngine.Services.Recovery;
using Infrastructure.Services.FlowEngine.Services.Security;
using Infrastructure.Services.FlowEngine.Services.Validation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Services.FlowEngine.Extensions
{
    /// <summary>
    /// Extension methods for setting up FlowEngine services in DI container
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Add FlowEngine services to the DI container - resolves configuration from User Secrets
        /// </summary>
        public static IServiceCollection AddFlowEngine(this IServiceCollection services, IConfiguration configuration, Action<FlowEngineConfiguration> configure = null)
        {
            var config = new FlowEngineConfiguration();

            // Bind FlowEngine configuration from User Secrets/appsettings
            var flowEngineSection = configuration.GetSection("FlowEngine");
            if (flowEngineSection.Exists())
            {
                var connectionString = flowEngineSection["ConnectionString"];
                var databaseName = flowEngineSection["DatabaseName"];

                if (!string.IsNullOrEmpty(connectionString))
                {
                    config.PersistenceType = PersistenceType.MongoDB;
                    config.ConnectionString = connectionString;
                }

                if (!string.IsNullOrEmpty(databaseName))
                {
                    config.DatabaseName = databaseName;
                }
            }

            // Allow additional configuration overrides
            configure?.Invoke(config);

            services.AddSingleton(config);
            services.AddLogging();
            services.AddMemoryCache();

            // Register core services
            services.AddScoped<IFlowExecutor, FlowExecutor>();
            services.AddScoped<IFlowEngineService, FlowEngineService>();
            services.AddScoped<IFlowValidation, FlowValidationService>();
            services.AddScoped<IFlowSecurity, FlowSecurityService>();
            services.AddScoped<IFlowRecovery, FlowRecoveryService>();
            services.AddScoped<IFlowMetrics, FlowMetricsService>();

            // Register pause/resume services
            services.AddScoped<IFlowEventService, FlowEventService>();
            services.AddScoped<IFlowAutoResumeService, FlowAutoResumeService>();

            // ✅ Automatically register all flows
            services.AddFlows()
                .ValidateFlowRegistrations();

            // Register persistence based on configuration
            services.AddFlowPersistence(config);

            // Register middleware
            services.AddFlowMiddleware(config);

            // Register security services
            services.AddFlowSecurity(config);

            return services;
        }

        private static IServiceCollection AddFlowPersistence(this IServiceCollection services, FlowEngineConfiguration config)
        {
            switch (config.PersistenceType)
            {
                case PersistenceType.MongoDB:
                    services.AddScoped<IFlowPersistence, MongoFlowPersistence>();
                    break;
                case PersistenceType.SqlServer:
                    services.AddScoped<IFlowPersistence, SqlServerFlowPersistence>();
                    break;
                case PersistenceType.InMemory:
                    services.AddSingleton<IFlowPersistence, InMemoryFlowPersistence>();
                    break;
            }

            return services;
        }

        private static IServiceCollection AddFlowMiddleware(this IServiceCollection services, FlowEngineConfiguration config)
        {
            // Register global middleware
            foreach (var middlewareType in config.GlobalMiddleware)
            {
                services.AddScoped(typeof(IFlowMiddleware), middlewareType);
            }

            // Register built-in middleware
            services.AddScoped<LoggingMiddleware>();
            services.AddScoped<SecurityMiddleware>();
            services.AddScoped<ValidationMiddleware>();
            services.AddScoped<PersistenceMiddleware>();
            services.AddScoped<MetricsMiddleware>();
            services.AddScoped<RetryMiddleware>();
            services.AddScoped<TimeoutMiddleware>();

            return services;
        }

        private static IServiceCollection AddFlowSecurity(this IServiceCollection services, FlowEngineConfiguration config)
        {
            if (config.Security.EnableEncryption)
            {
                services.AddScoped<IFlowEncryption, FlowEncryptionService>();
            }

            if (config.Security.EnableAuditLog)
            {
                services.AddScoped<IFlowAuditLog, FlowAuditLogService>();
            }

            return services;
        }
    }
}