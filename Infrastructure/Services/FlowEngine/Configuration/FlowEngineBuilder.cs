using Infrastructure.Services.FlowEngine.Configuration.Options;
using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Core.Interfaces;
using Infrastructure.Services.FlowEngine.Engine;
using Infrastructure.Services.FlowEngine.Middleware;
using Infrastructure.Services.FlowEngine.Services.Events;
using Infrastructure.Services.FlowEngine.Services.Metrics;
using Infrastructure.Services.FlowEngine.Services.Persistence;
using Infrastructure.Services.FlowEngine.Services.Recovery;
using Infrastructure.Services.FlowEngine.Services.Security;
using Infrastructure.Services.FlowEngine.Services.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Services.FlowEngine.Configuration
{
    /// <summary>
    /// Fluent builder for dead-simple FlowEngine configuration
    /// </summary>
    public class FlowEngineBuilder
    {
        private readonly IServiceCollection _services = new ServiceCollection();
        private readonly FlowEngineConfiguration _config = new();

        /// <summary>
        /// Use MongoDB for persistence (recommended for complex flows)
        /// </summary>
        public FlowEngineBuilder UseMongoDB(string connectionString, string databaseName = "FlowEngine")
        {
            _config.PersistenceType = PersistenceType.MongoDB;
            _config.ConnectionString = connectionString;
            _config.DatabaseName = databaseName;
            return this;
        }

        /// <summary>
        /// Use SQL Server for persistence (recommended for ACID requirements)
        /// </summary>
        public FlowEngineBuilder UseSqlServer(string connectionString)
        {
            _config.PersistenceType = PersistenceType.SqlServer;
            _config.ConnectionString = connectionString;
            return this;
        }

        /// <summary>
        /// Use in-memory storage (development/testing only)
        /// </summary>
        public FlowEngineBuilder UseInMemory()
        {
            _config.PersistenceType = PersistenceType.InMemory;
            return this;
        }

        /// <summary>
        /// Configure automatic recovery settings
        /// </summary>
        public FlowEngineBuilder WithRecovery(TimeSpan? interval = null, bool enableStartupRecovery = true)
        {
            _config.RecoveryInterval = interval ?? TimeSpan.FromMinutes(5);
            _config.EnableStartupRecovery = enableStartupRecovery;
            return this;
        }

        /// <summary>
        /// Configure security settings
        /// </summary>
        public FlowEngineBuilder WithSecurity(Action<FlowSecurityOptions> configure = null)
        {
            configure?.Invoke(_config.Security);
            return this;
        }

        /// <summary>
        /// Configure performance settings
        /// </summary>
        public FlowEngineBuilder WithPerformance(Action<FlowPerformanceOptions> configure = null)
        {
            configure?.Invoke(_config.Performance);
            return this;
        }

        /// <summary>
        /// Add custom middleware to all flows
        /// </summary>
        public FlowEngineBuilder UseMiddleware<TMiddleware>() where TMiddleware : class, IFlowMiddleware
        {
            _config.GlobalMiddleware.Add(typeof(TMiddleware));
            return this;
        }

        /// <summary>
        /// Register custom services
        /// </summary>
        public FlowEngineBuilder AddServices(Action<IServiceCollection> configure)
        {
            configure(_services);
            return this;
        }

        /// <summary>
        /// Build and initialize the FlowEngine
        /// </summary>
        public IServiceProvider Build()
        {
            // Register core services
            RegisterCoreServices();

            // Register persistence based on configuration
            RegisterPersistenceServices();

            // Register middleware pipeline
            RegisterMiddlewareServices();

            // Register security services
            RegisterSecurityServices();

            // Build service provider
            var serviceProvider = _services.BuildServiceProvider();

            // Initialize FlowEngine
            Engine.FlowEngine.Initialize(serviceProvider, _config);

            // Start background services if configured
            StartBackgroundServices(serviceProvider);

            return serviceProvider;
        }

        private void RegisterCoreServices()
        {
            _services.AddSingleton(_config);
            _services.AddLogging();
            _services.AddMemoryCache();

            _services.AddScoped<IFlowExecutor, FlowExecutor>();
            _services.AddScoped<IFlowEngineService, FlowEngineService>();
            _services.AddScoped<IFlowValidation, FlowValidationService>();
            _services.AddScoped<IFlowSecurity, FlowSecurityService>();
            _services.AddScoped<IFlowRecovery, FlowRecoveryService>();
            _services.AddScoped<IFlowMetrics, FlowMetricsService>();

            // NEW: Pause/Resume services
            _services.AddSingleton<IFlowEventService, FlowEventService>();
            _services.AddSingleton<IFlowAutoResumeService, FlowAutoResumeService>();
        }

        private void RegisterPersistenceServices()
        {
            switch (_config.PersistenceType)
            {
                case PersistenceType.MongoDB:
                    _services.AddScoped<IFlowPersistence, MongoFlowPersistence>();
                    break;
                case PersistenceType.SqlServer:
                    _services.AddScoped<IFlowPersistence, SqlServerFlowPersistence>();
                    break;
                case PersistenceType.InMemory:
                    _services.AddSingleton<IFlowPersistence, InMemoryFlowPersistence>();
                    break;
            }
        }

        private void RegisterMiddlewareServices()
        {
            // Register global middleware
            foreach (var middlewareType in _config.GlobalMiddleware)
            {
                _services.AddScoped(typeof(IFlowMiddleware), middlewareType);
            }

            // Register built-in middleware
            _services.AddScoped<LoggingMiddleware>();
            _services.AddScoped<SecurityMiddleware>();
            _services.AddScoped<ValidationMiddleware>();
            _services.AddScoped<PersistenceMiddleware>();
            _services.AddScoped<MetricsMiddleware>();
            _services.AddScoped<RetryMiddleware>();
            _services.AddScoped<TimeoutMiddleware>();
        }

        private void RegisterSecurityServices()
        {
            if (_config.Security.EnableEncryption)
            {
                _services.AddScoped<IFlowEncryption, FlowEncryptionService>();
            }

            if (_config.Security.EnableAuditLog)
            {
                _services.AddScoped<IFlowAuditLog, FlowAuditLogService>();
            }
        }

        private void StartBackgroundServices(IServiceProvider serviceProvider)
        {
            if (_config.EnableStartupRecovery)
            {
                Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(30)); // Wait for startup
                    await Engine.FlowEngine.RecoverCrashedFlows();
                });
            }

            // NEW: Start auto-resume background service
            Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(60)); // Wait for startup
                var autoResumeService = serviceProvider.GetRequiredService<IFlowAutoResumeService>();
                await autoResumeService.StartBackgroundCheckingAsync();
            });
        }
    }
}
