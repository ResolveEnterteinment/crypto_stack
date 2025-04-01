using Domain.DTOs;
using Domain.DTOs.Exchange;
using Domain.DTOs.Settings;
using Encryption;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace crypto_investment_project.Server.Configuration;

public static class AppSettingsExtensions
{
    public static IServiceCollection AddAppSettings(this IServiceCollection services, IConfiguration configuration)
    {
        // Register a global serializer to ensure GUIDs are stored using the Standard representation
        BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));

        // Configure settings sections
        services.Configure<MongoDbSettings>(configuration.GetSection("MongoDB"));
        services.Configure<ExchangeServiceSettings>(configuration.GetSection("ExchangeService"));
        services.Configure<BinanceSettings>(configuration.GetSection("Binance"));
        services.Configure<PaymentServiceSettings>(configuration.GetSection("PaymentService"));
        services.Configure<StripeSettings>(configuration.GetSection("Stripe"));
        services.Configure<JwtSettings>(configuration.GetSection("Jwt"));
        services.Configure<EncryptionSettings>(configuration.GetSection("Encryption"));

        // Add encryption services
        services.AddEncryptionServices();

        return services;
    }
}