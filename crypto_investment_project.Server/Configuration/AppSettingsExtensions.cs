using Domain.DTOs;
using Domain.DTOs.Exchange;
using Domain.DTOs.Settings;

namespace crypto_investment_project.Server.Configuration;

public static class AppSettingsExtensions
{
    public static IServiceCollection AddAppSettings(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure general app settings first
        services.Configure<AppSettings>(configuration.GetSection("App"));

        // Configure settings sections
        services.Configure<MongoDbSettings>(configuration.GetSection("MongoDB"));
        services.Configure<EmailSettings>(configuration.GetSection("EmailSettings"));
        services.Configure<ExchangeServiceSettings>(configuration.GetSection("ExchangeService"));
        services.Configure<BinanceSettings>(configuration.GetSection("Binance"));
        services.Configure<PaymentServiceSettings>(configuration.GetSection("PaymentService"));
        services.Configure<StripeSettings>(configuration.GetSection("Stripe"));
        services.Configure<JwtSettings>(configuration.GetSection("Jwt"));
        services.Configure<EncryptionSettings>(configuration.GetSection("Encryption"));
        services.Configure<WithdrawalServiceSettings>(configuration.GetSection("WithdrawalServiceSettings"));

        return services;
    }
}