using Domain.DTOs;
using Domain.DTOs.Exchange;
using Domain.DTOs.Settings;
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
        _ = services.Configure<MongoDbSettings>(configuration.GetSection("MongoDB"));
        _ = services.Configure<EmailSettings>(configuration.GetSection("EmailSettings"));
        _ = services.Configure<ExchangeServiceSettings>(configuration.GetSection("ExchangeService"));
        _ = services.Configure<BinanceSettings>(configuration.GetSection("Binance"));
        _ = services.Configure<PaymentServiceSettings>(configuration.GetSection("PaymentService"));
        _ = services.Configure<StripeSettings>(configuration.GetSection("Stripe"));
        _ = services.Configure<JwtSettings>(configuration.GetSection("Jwt"));
        _ = services.Configure<EncryptionSettings>(configuration.GetSection("Encryption"));

        return services;
    }
}