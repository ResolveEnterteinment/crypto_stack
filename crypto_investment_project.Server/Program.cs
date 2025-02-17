using Application.Interfaces;
using AspNetCore.Identity.MongoDbCore.Infrastructure;
using BinanceLibrary;
using crypto_investment_project.Server.Helpers;
using Domain.DTOs;
using Infrastructure.Services;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;

// Register a global serializer to ensure GUIDs are stored using the Standard representation.
BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));

var builder = WebApplication.CreateBuilder(args);

// Add User Secrets in development.
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>();
}

// Add services to the container.
MongoDbIdentityConfigurationHelper.Configure(builder);

// Bind configuration sections.
builder.Services.Configure<MongoDbSettings>(builder.Configuration.GetSection("MongoDB"));
builder.Services.Configure<BinanceSettings>(builder.Configuration.GetSection("Binance"));

// Register a singleton MongoClient using the connection string from MongoDbSettings.
builder.Services.AddSingleton<IMongoClient>(sp =>
{
    var mongoSettings = sp.GetRequiredService<IOptions<MongoDbSettings>>().Value;
    return new MongoClient(mongoSettings.ConnectionString);
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register custom services.
// Note: Services depending on HttpClient are registered via AddHttpClient.
builder.Services.AddHttpClient<CoinService>();
builder.Services.AddSingleton<ICoinService, CoinService>();
builder.Services.AddSingleton<IBinanceService, BinanceService>();
builder.Services.AddSingleton<IExchangeService, ExchangeService>();
builder.Services.AddSingleton<ISubscriptionService, SubscriptionService>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();

app.MapControllers();
app.MapFallbackToFile("/index.html");

app.Run();
