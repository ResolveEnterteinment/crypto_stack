using Application.Behaviors;
using Application.Interfaces;
using Application.Interfaces.Asset;
using Application.Interfaces.Base;
using Application.Interfaces.Exchange;
using Application.Interfaces.KYC;
using Application.Interfaces.Logging;
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
using Infrastructure.Services.KYC;
using Infrastructure.Services.Logging;
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
        services.AddDataProtection();
        services.AddSingleton(TimeProvider.System);
        services.AddHttpClient();
        services.AddMemoryCache();

        // Configure MongoDB client with connection pooling
        services.AddSingleton<IMongoClient>(provider =>
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
        services.AddFluentValidators(
            typeof(CoreServicesExtensions).Assembly,
            typeof(FluentValidators).Assembly);

        // Configure SignalR
        services.AddSignalR(options =>
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
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(EventService).Assembly);
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        });

        services.AddHttpClient<IKycService, OnfidoKycService>();

        services.AddOpenTelemetry()
        .WithTracing(tracerProviderBuilder =>
        {
            tracerProviderBuilder
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
        services.AddScoped(typeof(ICrudRepository<>), typeof(Repository<>));
        services.AddScoped(typeof(ICacheService<>), typeof(CacheService<>));
        services.AddScoped(typeof(IMongoIndexService<>), typeof(MongoIndexService<>));

        // Register services with appropriate lifecycles
        services.AddSingleton(typeof(IMongoIndexService<>), typeof(MongoIndexService<>));
        services.AddScoped<IEncryptionService, Encryption.Services.EncryptionService>();

        services.AddScoped<ILoggingService, LoggingService>();

        // 2) Your existing registrations
        services.AddScoped<IExchangeService, ExchangeService>();
        services.AddScoped<IPaymentProcessingService, PaymentProcessingService>();
        services.AddScoped<IOrderManagementService, OrderManagementService>();
        services.AddScoped<IBalanceManagementService, BalanceManagementService>();
        services.AddScoped<IOrderReconciliationService, OrderReconciliationService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IPaymentWebhookHandler, StripeWebhookHandler>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        services.AddScoped<IAssetService, AssetService>();
        services.AddScoped<IBalanceService, BalanceService>();
        services.AddScoped<ITransactionService, TransactionService>();
        services.AddScoped<IEventService, EventService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<ILogExplorerService, LogExplorerService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IIdempotencyService, IdempotencyService>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IKycService, OnfidoKycService>();
        services.AddScoped<IWithdrawalService, WithdrawalService>();

        services.AddScoped<ITestService, TestService>();
    }
}