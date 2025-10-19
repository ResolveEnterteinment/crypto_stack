using Application.Behaviors;
using Application.Interfaces;
using Application.Interfaces.Asset;
using Application.Interfaces.Base;
using Application.Interfaces.Exchange;
using Application.Interfaces.Logging;
using Application.Interfaces.Network;
using Application.Interfaces.Payment;
using Application.Interfaces.Subscription;
using Application.Interfaces.Treasury;
using Application.Interfaces.Withdrawal;
using Application.Validation;
using Domain.DTOs.Settings;
using Infrastructure.Flows.Demo;
using Infrastructure.Services;
using Infrastructure.Services.Asset;
using Infrastructure.Services.Base;
using Infrastructure.Services.Demo;
using Infrastructure.Services.Email;
using Infrastructure.Services.Exchange;
using Infrastructure.Services.Index;
using Infrastructure.Services.Logging;
using Infrastructure.Services.Network;
using Infrastructure.Services.Payment;
using Infrastructure.Services.Subscription;
using Infrastructure.Services.Transaction;
using Infrastructure.Services.Treasury;
using Infrastructure.Services.Withdrawal;
using MediatR;
using Microsoft.AspNetCore.DataProtection;
using MongoDB.Driver;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace crypto_investment_project.Server.Configuration;

public static class CoreServicesExtensions
{
    public static IServiceCollection AddCoreServices(this IServiceCollection services, IWebHostEnvironment environment)
    {
        // Register additional dependencies
        services.AddDataProtection()
            .SetApplicationName("CryptoInvestmentProject")
            .SetDefaultKeyLifetime(TimeSpan.FromDays(90));
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

        // Register MongoDB Database - FIXED: Added missing IMongoDatabase registration
        services.AddSingleton(provider =>
        {
            var mongoClient = provider.GetRequiredService<IMongoClient>();
            var configuration = provider.GetRequiredService<IConfiguration>();
            var databaseName = configuration["MongoDB:DatabaseName"];

            if (string.IsNullOrEmpty(databaseName))
            {
                throw new InvalidOperationException("MongoDB database name not found in configuration");
            }

            return mongoClient.GetDatabase(databaseName);
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
        services.AddSignalR(options =>
        {
            options.EnableDetailedErrors = environment.IsDevelopment();
            options.MaximumReceiveMessageSize = 102400; // 100KB
            options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
            options.KeepAliveInterval = TimeSpan.FromSeconds(15);
            options.HandshakeTimeout = TimeSpan.FromSeconds(15);
        })
        .AddJsonProtocol(options =>
        {
            options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.PayloadSerializerOptions.WriteIndented = false;
            options.PayloadSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
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
        // Register the generic resilience service
        _ = services.AddScoped(typeof(IResilienceService<>), typeof(ResilienceService<>));
        // Register a non-generic resilience service using a concrete implementation
        _ = services.AddScoped<IResilienceService>(serviceProvider =>
            serviceProvider.GetRequiredService<IResilienceService<object>>());
        _ = services.AddScoped(typeof(IEventService), typeof(EventService));
        _ = services.AddScoped(typeof(ICrudRepository<>), typeof(Repository<>));
        _ = services.AddScoped(typeof(ICacheService<>), typeof(CacheService<>));
        _ = services.AddScoped(typeof(IMongoIndexService<>), typeof(MongoIndexService<>));
        _ = services.AddScoped<ILoggingService, LoggingService>();

        // Register services with appropriate lifecycles
        _ = services.AddScoped<IEmailService, EmailService>();
        _ = services.AddScoped<IEncryptionService, Encryption.Services.EncryptionService>();


        // 2) Your existing registrations...
        services.AddScoped<IExchangeService, ExchangeService>();
        services.AddScoped<IPaymentProcessingService, PaymentProcessingService>();
        services.AddScoped<IOrderManagementService, OrderManagementService>();
        services.AddScoped<IBalanceManagementService, BalanceManagementService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IPaymentWebhookHandler, StripeWebhookHandler>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        services.AddScoped<ISubscriptionRetryService, SubscriptionRetryService>();
        services.AddScoped<IAssetService, AssetService>();
        services.AddScoped<INetworkService, NetworkService>();
        services.AddScoped<IBalanceService, BalanceService>();
        services.AddScoped<ITransactionService, TransactionService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<ILogExplorerService, LogExplorerService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IIdempotencyService, IdempotencyService>();
        services.AddScoped<IWithdrawalService, WithdrawalService>();
        services.AddScoped<ITreasuryService, TreasuryService>();
        services.AddScoped<ITreasuryBalanceService, TreasuryBalanceService>();
        services.AddScoped<IDemoService, DemoService>();
    }
}