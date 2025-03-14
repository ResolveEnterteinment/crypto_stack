using Application.Interfaces;
using AspNetCore.Identity.MongoDbCore.Infrastructure;
using crypto_investment_project.Server.Helpers;
using Domain.DTOs;
using Infrastructure.Hubs;
using Infrastructure.Services;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

// Register a global serializer to ensure GUIDs are stored using the Standard representation.
BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));

var builder = WebApplication.CreateBuilder(args);

// Configure MongoDB Identity, Data Protection, TimeProvider, and MongoClient
MongoDbIdentityConfigurationHelper.Configure(builder);

// Configure settings sections
builder.Services.Configure<MongoDbSettings>(builder.Configuration.GetSection("MongoDB"));
builder.Services.Configure<BinanceSettings>(builder.Configuration.GetSection("Binance"));
builder.Services.Configure<StripeSettings>(builder.Configuration.GetSection("Stripe"));
builder.Services.Configure<ExchangeSettings>(builder.Configuration.GetSection("Exchange"));
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
              .AllowAnyMethod();
    });
});


builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// -------------------------------------------------------------------
// Register additional dependencies required by your services:
// -------------------------------------------------------------------

// 1. Register HttpClient (so that System.Net.Http.HttpClient can be injected).
builder.Services.AddHttpClient();

// 3. Register your custom services.
builder.Services.AddSingleton<IPaymentService, PaymentService>();
builder.Services.AddSingleton<ISubscriptionService, SubscriptionService>();
builder.Services.AddScoped<IExchangeService, ExchangeService>();
builder.Services.AddSingleton<IAssetService, AssetService>();
builder.Services.AddSingleton<IBalanceService, BalanceService>();
builder.Services.AddSingleton<ITransactionService, TransactionService>();
builder.Services.AddSingleton<IEventService, EventService>();
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<INotificationService, NotificationService>();

builder.Services.AddSignalR();
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(EventService).Assembly));

var app = builder.Build();


app.UseDefaultFiles();
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowSpecifiedOrigins");
app.UseHttpsRedirection();
app.UseAuthorization();

app.MapControllers();
app.MapFallbackToFile("/index.html");
app.MapHub<NotificationHub>("/notificationHub"); // <-- Register the SignalR Hub

app.Run();