using Infrastructure.Services.FlowEngine.BackgroundServices;
using Infrastructure.Services.FlowEngine.Concurrency;
using Infrastructure.Services.FlowEngine.Core;
using Infrastructure.Services.FlowEngine.Events;
using Infrastructure.Services.FlowEngine.Execution;
using Infrastructure.Services.FlowEngine.Middleware;
using Infrastructure.Services.FlowEngine.Models;
using Infrastructure.Services.FlowEngine.Persistence;
using Infrastructure.Services.FlowEngine.Security;
using Infrastructure.Services.FlowEngine.Validation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace Infrastructure.Services.FlowEngine.Configuration
{
    /// <summary>
    /// Extension methods for dependency injection setup
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Add Flow Engine services to DI container with environment-aware security
        /// </summary>
        public static IServiceCollection AddFlowEngine(
            this IServiceCollection services,
            IConfiguration configuration,
            Action<FlowEngineBuilder> configure = null)
        {
            // Validate and bind configuration
            var optionsSection = configuration.GetSection("FlowEngine");
            services.Configure<FlowEngineOptions>(optionsSection);
            services.AddOptions<FlowEngineOptions>()
                .Bind(optionsSection)
                .ValidateDataAnnotations()
                .ValidateOnStart();

            // Create builder for fluent configuration
            var builder = new FlowEngineBuilder(services, configuration);
            configure?.Invoke(builder);

            // FIXED: Environment-aware security service registration
            var environment = configuration["ASPNETCORE_ENVIRONMENT"] ?? configuration["Environment"] ?? "Production";

            if (!builder.HasCustomSecurity)
            {
                if (environment.Equals("Development", StringComparison.OrdinalIgnoreCase))
                {
                    services.AddScoped<IFlowSecurity>(provider => new DefaultFlowSecurity(
                        provider.GetRequiredService<ILogger<DefaultFlowSecurity>>(),
                        provider.GetRequiredService<IOptions<FlowEngineOptions>>(),
                        provider.GetRequiredService<IConfiguration>()));

                    // Log security configuration decision
                    using var tempProvider = services.BuildServiceProvider();
                    var loggerFactory = tempProvider.GetService<ILoggerFactory>();
                    var logger = loggerFactory?.CreateLogger("FlowEngine.Configuration");
                    logger?.LogWarning("Using DefaultFlowSecurity in Development environment. Do not use in production!");
                }
                else
                {
                    services.AddScoped<IFlowSecurity, EnterpriseFlowSecurity>();

                    // Log security configuration decision
                    using var tempProvider = services.BuildServiceProvider();
                    var loggerFactory = tempProvider.GetService<ILoggerFactory>();
                    var logger = loggerFactory?.CreateLogger("FlowEngine.Configuration");
                    logger?.LogInformation("Using EnterpriseFlowSecurity for production environment");
                }
            }

            // Register core services  
            services.AddScoped<IFlowEngineService, FlowEngineService>();
            services.AddScoped<IFlowExecutor, FlowExecutor>();
            services.AddScoped<IFlowValidation, FlowValidationService>();
            services.AddScoped<IFlowEventService, EnhancedFlowEventService>();
            services.AddScoped<IFlowAuditService, FlowAuditService>();
            services.AddScoped<IStepExecutionTracker, ThreadSafeStepExecutionTracker>();
            services.AddScoped<IFlowAutoResumeService, FlowAutoResumeService>();
            services.AddSingleton<IConcurrencyControlService, ConcurrencyControlService>();
            services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();

            // Register middleware
            services.AddScoped<IFlowMiddleware, FlowLoggingMiddleware>();
            services.AddScoped<IFlowMiddleware, FlowSecurityMiddleware>();
            services.AddScoped<IFlowMiddleware, FlowPerformanceMiddleware>();

            // Register stub identity and key vault services
            //services.AddScoped<IIdentityService, StubIdentityService>();
            //services.AddScoped<IKeyVaultService, StubKeyVaultService>();

            // Register persistence based on configuration
            var options = optionsSection.Get<FlowEngineOptions>();
            if (options?.Persistence?.Type != null)
            {
                RegisterPersistenceServices(services, options.Persistence.Type);
            }

            // Register background services
            services.AddHostedService<AutoResumeWorker>();
            services.AddHostedService<BackgroundTaskProcessor>();

            // Initialize static facade with service provider (not service instance)
            services.AddSingleton<IHostedService>(provider =>
            {
                Infrastructure.Services.FlowEngine.Core.FlowEngine.Initialize(provider);
                return new FlowEngineInitializer();
            });

            // Add observability
            if (options?.Observability?.EnableTracing == true)
            {
                services.AddSingleton(new ActivitySource(options.Observability.ActivitySourceName));
            }

            return services;
        }

        private static void RegisterPersistenceServices(IServiceCollection services, PersistenceType persistenceType)
        {
            switch (persistenceType)
            {
                case PersistenceType.InMemory:
                    services.AddSingleton<IFlowPersistence, InMemoryFlowPersistence>();
                    break;
                case PersistenceType.SqlServer:
                    services.AddScoped<IFlowPersistence, SqlServerFlowPersistence>();
                    break;
                case PersistenceType.PostgreSQL:
                    services.AddScoped<IFlowPersistence, PostgreSQLFlowPersistence>();
                    break;
                case PersistenceType.MongoDB:
                    services.AddScoped<IFlowPersistence, MongoFlowPersistence>();
                    break;
                case PersistenceType.CosmosDB:
                    services.AddScoped<IFlowPersistence, CosmosFlowPersistence>();
                    break;
                default:
                    throw new NotSupportedException($"Persistence type {persistenceType} is not supported");
            }
        }
    }

    /// <summary>
    /// Hosted service to initialize static facade
    /// </summary>
    internal sealed class FlowEngineInitializer : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}