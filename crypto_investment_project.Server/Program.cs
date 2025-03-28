using Application.Interfaces;
using Application.Interfaces.Exchange;
using Application.Interfaces.Payment;
using Application.Validation;
using crypto_investment_project.Server.Helpers;
using crypto_investment_project.Server.Middleware;
using Domain.DTOs;
using Domain.DTOs.Exchange;
using FluentValidation;
using Infrastructure.Hubs;
using Infrastructure.Services;
using Infrastructure.Services.Exchange;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using System.Text;

// Register a global serializer to ensure GUIDs are stored using the Standard representation.
BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));

var builder = WebApplication.CreateBuilder(args);

// Configure MongoDB Identity, Data Protection, TimeProvider, and MongoClient
MongoDbIdentityConfigurationHelper.Configure(builder);

// Configure settings sections
builder.Services.Configure<MongoDbSettings>(builder.Configuration.GetSection("MongoDB"));
builder.Services.Configure<ExchangeServiceSettings>(builder.Configuration.GetSection("ExchangeService"));
builder.Services.Configure<BinanceSettings>(builder.Configuration.GetSection("Binance"));
builder.Services.Configure<StripeSettings>(builder.Configuration.GetSection("Stripe"));
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));

// Fetch allowed origins from user secrets
var allowedOrigins = builder.Configuration["AllowedOrigins"]?
    .Split(';', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();

// Add controllers with Newtonsoft.Json
builder.Services.AddControllers()
    .AddNewtonsoftJson(options =>
    {
        options.SerializerSettings.ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore;
        options.SerializerSettings.NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore;
    });

// Configure CORS with origins from configuration
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecifiedOrigins", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // Allow credentials for WebSockets
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add JWT Authentication
builder.Services.AddAuthentication(options =>
{
    // Identity made Cookie authentication the default.
    // However, we want JWT Bearer Auth to be the default.
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
    .AddJwtBearer(options =>
    {
        // Configure the Authority to the expected value for
        // the authentication provider. This ensures the token
        // is appropriately validated.
        //options.Authority = "https://localhost:5173/auth"; // TODO: Update URL
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"], // "crypto-stack"
            ValidAudience = builder.Configuration["Jwt:Audience"], // "crypto-stack"
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]))
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
                if (!string.IsNullOrEmpty(accessToken) &&
                    context.Request.Path.StartsWithSegments("/hubs/notificationHub"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            },
            OnAuthenticationFailed = context =>
            {
                Console.WriteLine($"Authentication failed: {context.Exception.Message}");
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                Console.WriteLine("JWT Token validated successfully");
                return Task.CompletedTask;
            }
        };
    });

// Register additional dependencies
builder.Services.AddDataProtection();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient();

// Register Fluent Validation
builder.Services.AddValidatorsFromAssemblyContaining<FluentValidator>();

builder.Services.AddValidators();

// Register services for DI
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
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IIdempotencyService, IdempotencyService>();

builder.Services.AddSignalR();
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(EventService).Assembly));

var app = builder.Build();

// For debugging purposes (optional)
var exchanges = builder.Configuration.GetSection("ExchangeService").Get<ExchangeServiceSettings>();

app.UseDefaultFiles();
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Add global exception handling middleware
app.UseGlobalExceptionHandling();

app.UseCors("AllowSpecifiedOrigins");
app.UseAuthentication();
app.UseHttpsRedirection();
app.UseAuthorization();

app.MapHub<NotificationHub>("/hubs/notificationHub");
app.MapControllers();
app.MapFallbackToFile("/index.html");

app.Run();