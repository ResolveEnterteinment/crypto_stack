using Application.Behaviors;
using Application.Interfaces;
using Application.Interfaces.Asset;
using Application.Interfaces.Base;
using Application.Interfaces.Exchange;
using Application.Interfaces.Logging;
using Application.Interfaces.Network;
using Application.Interfaces.Payment;
using Application.Interfaces.Subscription;
using Application.Interfaces.Withdrawal;
using Application.Validation;
using Domain.DTOs.Settings;
using Domain.Interfaces;
using Infrastructure;
using Infrastructure.Services;
using Infrastructure.Services.Asset;
using Infrastructure.Services.Base;
using Infrastructure.Services.Event;
using Infrastructure.Services.Exchange;
using Infrastructure.Services.Index;
using Infrastructure.Services.Logging;
using Infrastructure.Services.Network;
using Infrastructure.Services.Payment;
using Infrastructure.Services.Subscription;
using Infrastructure.Services.Withdrawal;
using MediatR;
using MongoDB.Driver;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

namespace crypto_investment_project.Server.Configuration;

public static class CoreServicesExtensions
{
    public static IServiceCollection AddCoreServices(this IServiceCollection services, IWebHostEnvironment environment)
    {
        // Register additional dependencies
        _ = services.AddDataProtection();
        _ = services.AddSingleton(TimeProvider.System);
        _ = services.AddHttpClient();
        _ = services.AddMemoryCache();

        // Configure MongoDB client with connection pooling
        _ = services.AddSingleton<IMongoClient>(provider =>
        {
            var settings = provider.GetRequiredService<IConfiguration>().GetSection("MongoDB").Get<MongoDbSettings>();
            if (settings == null || string.IsNullOrEmpty(settings.ConnectionString))
            {
                throw new InvalidOperationException("MongoDB connection string not found in configuration");
            }

            var mongoClientSettings = MongoClientSettings.FromUrl(new MongoUrl(settings.ConnectionString));

            // Configure connection pooling
            mongoClientSettings.MaxConnectionPoolSize = 100;
            mongoClientSettings.MinConnectionPoolSize = 5;
            mongoClientSettings.MaxConnectionIdleTime = TimeSpan.FromMinutes(10);
            mongoClientSettings.MaxConnectionLifeTime = TimeSpan.FromHours(1);

            // Configure retry logic
            mongoClientSettings.RetryReads = true;
            mongoClientSettings.RetryWrites = true;

            return new MongoClient(mongoClientSettings);
        });

        Log.Logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .Enrich.WithEnvironmentName()
            .Enrich.WithThreadId()
            .Enrich.WithProcessId()
            .Enrich.With<TracingEnricher>()
            .WriteTo.Console()
            .WriteTo.MongoDBBson("mongodb://localhost:27017/logs", collectionName: "TraceLogs")
            .CreateLogger();

        // Register Fluent Validation
        _ = services.AddFluentValidators(
            typeof(CoreServicesExtensions).Assembly,
            typeof(FluentValidators).Assembly);

        // Configure SignalR
        _ = services.AddSignalR(options =>
        {
            options.EnableDetailedErrors = environment.IsDevelopment();
            options.MaximumReceiveMessageSize = 102400; // 100 KB
            options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
            options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        })
            .AddJsonProtocol(options =>
            {
                options.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
            });

        // Add MediatR
        _ = services.AddMediatR(cfg =>
        {
            _ = cfg.RegisterServicesFromAssembly(typeof(EventService).Assembly);
            _ = cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        });

        _ = services.AddOpenTelemetry()
        .WithTracing(tracerProviderBuilder =>
        {
            _ = tracerProviderBuilder
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddSource("StackFi") // ActivitySource name
                .SetResourceBuilder(
                    ResourceBuilder.CreateDefault()
                        .AddService("Server"))
                .AddOtlpExporter(otlpOptions =>
                {
                    otlpOptions.Endpoint = new Uri("http://localhost:4317"); // default OTLP gRPC collector
                    otlpOptions.Protocol = OtlpExportProtocol.Grpc; // or HttpProtobuf if you prefer
                })

                // Or Zipkin exporter:
                .AddZipkinExporter(options =>
                 {
                     options.Endpoint = new Uri("http://localhost:9411/api/v2/spans");
                 }
                 );
        });

        // Register application services
        RegisterApplicationServices(services);

        return services;
    }

    private static void RegisterApplicationServices(IServiceCollection services)
    {
        // 1) Generic plumbing for all BaseService<T> consumers:
        _ = services.AddScoped(typeof(ICrudRepository<>), typeof(Repository<>));
        _ = services.AddScoped(typeof(ICacheService<>), typeof(CacheService<>));
        _ = services.AddScoped(typeof(IMongoIndexService<>), typeof(MongoIndexService<>));

        // Register services with appropriate lifecycles
        _ = services.AddSingleton(typeof(IMongoIndexService<>), typeof(MongoIndexService<>));
        _ = services.AddScoped<IEmailService, EmailService>();
        _ = services.AddScoped<IEncryptionService, Encryption.Services.EncryptionService>();

        _ = services.AddScoped<ILoggingService, LoggingService>();

        // 2) Your existing registrations
        _ = services.AddScoped<IExchangeService, ExchangeService>();
        _ = services.AddScoped<IPaymentProcessingService, PaymentProcessingService>();
        _ = services.AddScoped<IOrderManagementService, OrderManagementService>();
        _ = services.AddScoped<IBalanceManagementService, BalanceManagementService>();
        _ = services.AddScoped<IOrderReconciliationService, OrderReconciliationService>();
        _ = services.AddScoped<IPaymentService, PaymentService>();
        _ = services.AddScoped<IPaymentWebhookHandler, StripeWebhookHandler>();
        _ = services.AddScoped<ISubscriptionService, SubscriptionService>();
        _ = services.AddScoped<IAssetService, AssetService>();
        _ = services.AddScoped<INetworkService, NetworkService>();
        _ = services.AddScoped<IBalanceService, BalanceService>();
        _ = services.AddScoped<ITransactionService, TransactionService>();
        _ = services.AddScoped<IEventService, EventService>();
        _ = services.AddScoped<IDashboardService, DashboardService>();
        _ = services.AddScoped<IAuthenticationService, AuthenticationService>();
        _ = services.AddScoped<IUserService, UserService>();
        _ = services.AddScoped<ILogExplorerService, LogExplorerService>();
        _ = services.AddScoped<INotificationService, NotificationService>();
        _ = services.AddScoped<IIdempotencyService, IdempotencyService>();
        _ = services.AddScoped<IUnitOfWork, UnitOfWork>();
        _ = services.AddScoped<IWithdrawalService, WithdrawalService>();
        _ = services.AddScoped<ISubscriptionRetryService, SubscriptionRetryService>();

        _ = services.AddScoped<ITestService, TestService>();
    }
}