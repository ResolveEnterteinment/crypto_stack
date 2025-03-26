using Application.Interfaces;
using Application.Interfaces.Exchange;
using BinanceLibrary;
using DnsClient.Internal;
using Domain.DTOs;
using Domain.DTOs.Exchange;
using Domain.Models.Exchange;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Infrastructure.Services.Exchange
{
    public class ExchangeService : BaseService<ExchangeOrderData>, IExchangeService
    {
        private readonly Dictionary<string, IExchange> _exchanges = new Dictionary<string, IExchange>(StringComparer.OrdinalIgnoreCase);

        protected readonly IOptions<ExchangeServiceSettings> _exchangeServiceSettings;
        protected readonly IEventService _eventService;
        protected readonly ISubscriptionService _subscriptionService;
        protected readonly IBalanceService _balanceService;
        protected readonly ITransactionService _transactionService;
        protected readonly IAssetService _assetService;

        public Dictionary<string, IExchange> Exchanges { get => _exchanges; }
        public readonly Guid FiatAssetId = Guid.Empty;

        public ExchangeService(
            IOptions<ExchangeServiceSettings> exchangeServiceSettings,
            IOptions<MongoDbSettings> mongoDbSettings,
            IMongoClient mongoClient,
            IEventService eventService,
            ISubscriptionService subscriptionService,
            IBalanceService balanceService,
            ITransactionService transactionService,
            IAssetService assetService,
            ILogger<ExchangeService> logger) : base(mongoClient, mongoDbSettings, "exchange_orders", logger)
        {
            _exchangeServiceSettings = exchangeServiceSettings ?? throw new ArgumentNullException(nameof(exchangeServiceSettings));
            _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
            _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
            _balanceService = balanceService ?? throw new ArgumentNullException(nameof(balanceService));
            _transactionService = transactionService ?? throw new ArgumentNullException(nameof(transactionService));
            _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));

            if (_exchangeServiceSettings.Value == null || _exchangeServiceSettings.Value.ExchangeSettings == null)
            {
                _logger.LogWarning("No exchange settings configured. Exchange functionality will be limited.");
                return; // Allow the service to start with no exchanges
            }

            InitExchanges();
            FiatAssetId = Guid.Parse(_exchangeServiceSettings.Value.PlatformFiatAssetId);
        }

        private void InitExchanges()
        {
            foreach (var (exchangeName, settings) in _exchangeServiceSettings.Value.ExchangeSettings)
            {
                try
                {
                    switch (exchangeName.ToLowerInvariant())
                    {
                        case "binance":
                            _exchanges.Add(exchangeName, new BinanceService(settings, _logger));
                            _logger.LogInformation("Successfully initialized exchange: {ExchangeName}", exchangeName);
                            break;
                        // Add support for other exchanges here
                        default:
                            _logger.LogWarning("Unsupported exchange '{ExchangeName}' found in configuration. Skipping.", exchangeName);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to initialize exchange '{ExchangeName}'. Skipping.", exchangeName);
                }
            }

            if (!_exchanges.Any())
            {
                _logger.LogWarning("No valid exchanges were initialized. Exchange functionality will be unavailable.");
            }
        }
        public async Task<IClientSessionHandle> StartDBSession()
        {
            return await _mongoClient.StartSessionAsync();
        }
    }
}