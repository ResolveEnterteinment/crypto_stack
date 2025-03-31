using Application.Behaviors;
using Application.Interfaces;
using Application.Interfaces.Exchange;
using Application.Interfaces.Payment;
using Application.Validation;
using crypto_investment_project.Server.Helpers;
using crypto_investment_project.Server.Middleware;
using Domain.DTOs;
using Domain.DTOs.Exchange;
using Domain.DTOs.Settings;
using Domain.Interfaces;
using Encryption;
using Encryption.Services;
using HealthChecks.UI.Client;
using Infrastructure;
using Infrastructure.HealthChecks;
using Infrastructure.Hubs;
using Infrastructure.Services;
using Infrastructure.Services.Exchange;
using MediatR;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Versioning;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using System.Text;
using System.Threading.RateLimiting;

// Register a global serializer to ensure GUIDs are stored using the Standard representation.
BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));

var builder = WebApplication.CreateBuilder(args);

// Apply configuration
ConfigureSettings(builder);

// Configure MongoDB Identity, Data Protection, TimeProvider, and MongoClient
MongoDbIdentityConfigurationHelper.Configure(builder);

// Configure Authentication and Authorization
ConfigureAuthenticationAndAuthorization(builder);

// Register Services
ConfigureServices(builder);

// Configure Rate Limiting
ConfigureRateLimiting(builder);

// Configure API Versioning
ConfigureApiVersioning(builder);

// Configure Health Checks
ConfigureHealthChecks(builder);

// Add controllers with Newtonsoft.Json
builder.Services.AddControllers(options =>
{
    // Register as a filter factory instead of a direct filter instance
    options.Filters.Add<AutoValidateAntiforgeryTokenAttribute>();
})
    .AddNewtonsoftJson(options =>
    {
        options.SerializerSettings.ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore;
        options.SerializerSettings.NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore;
    });

// Configure CORS with origins from configuration
ConfigureCors(builder);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Crypto Stack API",
        Version = "v1",
        Description = "API for managing cryptocurrency investments"
    });

    // Configure Swagger to use JWT
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "JWT Authorization header using the Bearer scheme."
    });

    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] {}
        }
    });
});

var app = builder.Build();

// Add global exception handling middleware
app.UseGlobalExceptionHandling();

app.UseDefaultFiles();
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Add security headers
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.Append("Content-Security-Policy",
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:;");
    await next();
});

// Apply rate limiting
app.UseRateLimiter();

// Add this to Program.cs after UseStaticFiles() and before UseCors()
app.Use(async (context, next) =>
{
    // Only for GET requests to set the cookie
    if (context.Request.Method == HttpMethods.Get)
    {
        var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
        // We set the cookie but don't need the token here
        antiforgery.GetAndStoreTokens(context);
    }

    await next();
});

app.UseCors("AllowSpecifiedOrigins");
app.UseAuthentication();
app.UseHttpsRedirection();
app.UseAuthorization();

// Map health checks endpoint
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    }
});

// Optional: Add a specific endpoint for readiness checks (used by orchestrators like Kubernetes)
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("readiness"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

// Optional: Add a specific endpoint for liveness checks
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.MapHub<NotificationHub>("/hubs/notificationHub");
app.MapControllers();
app.MapFallbackToFile("/index.html");

app.Run();


// Helper methods for configuration
void ConfigureSettings(WebApplicationBuilder builder)
{
    // Configure settings sections
    builder.Services.Configure<MongoDbSettings>(builder.Configuration.GetSection("MongoDB"));

    builder.Services.Configure<ExchangeServiceSettings>(builder.Configuration.GetSection("ExchangeService"));
    builder.Services.Configure<BinanceSettings>(builder.Configuration.GetSection("Binance"));

    builder.Services.Configure<PaymentServiceSettings>(builder.Configuration.GetSection("PaymentService"));
    builder.Services.Configure<StripeSettings>(builder.Configuration.GetSection("Stripe"));
    builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));

    // Add encryption settings
    builder.Services.Configure<EncryptionSettings>(builder.Configuration.GetSection("Encryption"));
}

void ConfigureAuthenticationAndAuthorization(WebApplicationBuilder builder)
{
    // Configure JWT Authentication
    var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>();

    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.Zero, // Reduce the default 5 minute skew for tighter security
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Key))
        };

        // Handle SignalR token from query string
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (string.IsNullOrEmpty(accessToken))
                {
                    accessToken = context.Request.Headers["Authorization"].FirstOrDefault()?.Replace("Bearer ", "");
                }

                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/notificationHub"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            },
            OnAuthenticationFailed = context =>
            {
                if (context.Exception.GetType() == typeof(SecurityTokenExpiredException))
                {
                    context.Response.Headers.Add("Token-Expired", "true");
                    context.Response.Headers.Add("Access-Control-Expose-Headers", "Token-Expired");
                }
                return Task.CompletedTask;
            }
        };
    });

    // Add Authorization policies
    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy("RequireAdminRole", policy => policy.RequireRole("ADMIN"));
        options.AddPolicy("RequireUserRole", policy => policy.RequireRole("USER"));
    });

    // Add Antiforgery protection
    builder.Services.AddAntiforgery(options =>
    {
        options.HeaderName = "X-CSRF-TOKEN";
        options.Cookie.Name = "XSRF-TOKEN";
        options.Cookie.HttpOnly = false; // Must be accessible from JavaScript
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    });

    // Add this line to ensure MVC services are fully registered
    builder.Services.AddMvc();
}

void ConfigureServices(WebApplicationBuilder builder)
{
    // Register additional dependencies
    builder.Services.AddDataProtection();
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddHttpClient();

    // Add caching
    builder.Services.AddMemoryCache();

    builder.Services.AddEncryptionServices();

    // Configure MongoDB client with connection pooling
    builder.Services.AddSingleton<IMongoClient>(provider =>
    {
        var settings = builder.Configuration.GetSection("MongoDB").Get<MongoDbSettings>();
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
    builder.Services.AddFluentValidators(
        typeof(Program).Assembly,
        typeof(FluentValidators).Assembly);

    // Register services for DI
    builder.Services.AddScoped<IEncryptionService, EncryptionService>();
    builder.Services.AddScoped<IExchangeService, ExchangeService>();
    builder.Services.AddScoped<IPaymentProcessingService, PaymentProcessingService>();
    builder.Services.AddScoped<IOrderManagementService, OrderManagementService>();
    builder.Services.AddScoped<IBalanceManagementService, BalanceManagementService>();
    builder.Services.AddScoped<IOrderReconciliationService, OrderReconciliationService>();

    builder.Services.AddScoped<IPaymentService, PaymentService>();
    builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();
    builder.Services.AddScoped<IAssetService, AssetService>();
    builder.Services.AddScoped<IBalanceService, BalanceService>();
    builder.Services.AddScoped<ITransactionService, TransactionService>();
    builder.Services.AddScoped<IEventService, EventService>();
    builder.Services.AddScoped<IDashboardService, DashboardService>();
    builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
    builder.Services.AddScoped<IUserService, UserService>();
    builder.Services.AddScoped<INotificationService, NotificationService>();
    builder.Services.AddScoped<IIdempotencyService, IdempotencyService>();
    builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

    builder.Services.AddSignalR(options =>
    {
        options.EnableDetailedErrors = builder.Environment.IsDevelopment();
        options.MaximumReceiveMessageSize = 102400; // 100 KB
    });

    builder.Services.AddMediatR(cfg =>
    {
        cfg.RegisterServicesFromAssembly(typeof(EventService).Assembly);
        cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
    });

}

// In the ConfigureRateLimiting method in Program.cs
void ConfigureRateLimiting(WebApplicationBuilder builder)
{
    builder.Services.AddRateLimiter(options =>
    {
        // Define the global rate limiter
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString()
                    ?? httpContext.Request.Headers["X-Forwarded-For"].ToString()
                    ?? "unknown",
                factory: partition => new FixedWindowRateLimiterOptions
                {
                    AutoReplenishment = true,
                    PermitLimit = 100,
                    QueueLimit = 0,
                    Window = TimeSpan.FromMinutes(1)
                }));

        // Login policy for general API endpoints
        options.AddPolicy("login", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString()
                    ?? httpContext.Request.Headers["X-Forwarded-For"].ToString()
                    ?? "unknown",
                factory: partition => new FixedWindowRateLimiterOptions
                {
                    AutoReplenishment = true,
                    PermitLimit = 60,
                    QueueLimit = 0,
                    Window = TimeSpan.FromMinutes(1)
                }));

        // Standard policy for general API endpoints
        options.AddPolicy("standard", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString()
                    ?? httpContext.Request.Headers["X-Forwarded-For"].ToString()
                    ?? "unknown",
                factory: partition => new FixedWindowRateLimiterOptions
                {
                    AutoReplenishment = true,
                    PermitLimit = 30,
                    QueueLimit = 0,
                    Window = TimeSpan.FromMinutes(1)
                }));

        // Heavy operations policy (already defined)
        options.AddPolicy("heavyOperations", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString()
                    ?? httpContext.Request.Headers["X-Forwarded-For"].ToString()
                    ?? "unknown",
                factory: partition => new FixedWindowRateLimiterOptions
                {
                    AutoReplenishment = true,
                    PermitLimit = 5,
                    QueueLimit = 2,
                    Window = TimeSpan.FromMinutes(1)
                }));

        // Special policy for authentication endpoints (already defined)
        options.AddPolicy("AuthEndpoints", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString()
                    ?? httpContext.Request.Headers["X-Forwarded-For"].ToString()
                    ?? "unknown",
                factory: partition => new FixedWindowRateLimiterOptions
                {
                    AutoReplenishment = true,
                    PermitLimit = 10,
                    QueueLimit = 0,
                    Window = TimeSpan.FromMinutes(1)
                }));

        // Special policy for payment webhooks (already defined)
        options.AddPolicy("webhook", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString()
                    ?? httpContext.Request.Headers["X-Forwarded-For"].ToString()
                    ?? "unknown",
                factory: partition => new FixedWindowRateLimiterOptions
                {
                    AutoReplenishment = true,
                    PermitLimit = 30,
                    QueueLimit = 5,
                    Window = TimeSpan.FromMinutes(1)
                }));

        // Configure on-rejected behavior
        options.OnRejected = async (context, token) =>
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.HttpContext.Response.WriteAsync("Too many requests. Please try again later.", token);
        };
    });
}

void ConfigureApiVersioning(WebApplicationBuilder builder)
{
    builder.Services.AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new ApiVersion(1, 0);
        options.AssumeDefaultVersionWhenUnspecified = true;
        options.ReportApiVersions = true;
        options.ApiVersionReader = ApiVersionReader.Combine(
            new UrlSegmentApiVersionReader(),
            new HeaderApiVersionReader("X-API-Version"),
            new QueryStringApiVersionReader("api-version"));
    });

    builder.Services.AddVersionedApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true;
    });
}

void ConfigureHealthChecks(WebApplicationBuilder builder)
{
    // In Program.cs
    builder.Services.AddHealthChecks()
    .AddCheck<ExchangeApiHealthCheck>("exchange_api", tags: new[] { "readiness" })
    .AddMongoDb(
        sp => new MongoClient(builder.Configuration["MongoDB:ConnectionString"]),
        name: "mongodb",
        tags: new[] { "readiness" },
        timeout: TimeSpan.FromSeconds(3));

}

// Update the ConfigureCors method to ensure it works with your frontend
// Update the ConfigureCors method to ensure it works with your frontend
void ConfigureCors(WebApplicationBuilder builder)
{
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowSpecifiedOrigins", policy =>
        {
            // In development, allow all local origins
            if (builder.Environment.IsDevelopment())
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
                var allowedOrigins = builder.Configuration["AllowedOrigins"]?
                    .Split(';', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();

                policy.WithOrigins(allowedOrigins)
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials()
                      .WithExposedHeaders("X-CSRF-TOKEN");
            }
        });
    });
}