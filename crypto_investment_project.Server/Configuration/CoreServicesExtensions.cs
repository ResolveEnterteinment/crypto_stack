using Application.Behaviors;
using Application.Interfaces;
using Application.Interfaces.Asset;
using Application.Interfaces.Base;
using Application.Interfaces.Exchange;
using Application.Interfaces.Payment;
using Application.Interfaces.Subscription;
using Application.Validation;
using Domain.DTOs.Settings;
using Domain.Interfaces;
using Infrastructure;
using Infrastructure.Services;
using Infrastructure.Services.Asset;
using Infrastructure.Services.Base;
using Infrastructure.Services.Exchange;
using Infrastructure.Services.Index;
using Infrastructure.Services.Payment;
using Infrastructure.Services.Subscription;
using MediatR;
using MongoDB.Driver;

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

        // Register Fluent Validation
        services.AddFluentValidators(
            typeof(CoreServicesExtensions).Assembly,
            typeof(FluentValidators).Assembly);

        // Register application services
        RegisterApplicationServices(services);

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
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IIdempotencyService, IdempotencyService>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
    }
}