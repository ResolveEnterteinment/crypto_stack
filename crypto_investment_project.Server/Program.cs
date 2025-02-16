using Application.Interfaces;
using AspNetCore.Identity.MongoDbCore.Infrastructure;
using crypto_investment_project.Server.Helpers;
using Domain.DTOs;
using Infrastructure.Services;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

// Register a global serializer to ensure GUIDs are stored using the Standard representation.
BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
MongoDbIdentityConfigurationHelper.Configure(builder);
builder.Services.Configure<MongoDbSettings>(builder.Configuration.GetSection("MongoDB"));
builder.Services.Configure<BinanceSettings>(builder.Configuration.GetSection("Binance"));

//builder.Services.AddControllers();
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register your custom services BEFORE calling Build()
builder.Services.AddSingleton<IExchangeService, ExchangeService>();
builder.Services.AddSingleton<ISubscriptionService, SubscriptionService>();
builder.Services.AddSingleton<ICoinService, CoinService>();

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
