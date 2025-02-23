using AspNetCore.Identity.MongoDbCore.Infrastructure;
using Domain.Models.Crypto;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Infrastructure.Services
{
    public class CoinService : BaseService<CoinData>, ICoinService
    {

        /// <summary>
        /// Initializes a new instance of the <see cref="CoinService"/> class.
        /// </summary>
        /// <param name="mongoDbSettings">MongoDB settings injected via IOptions.</param>
        /// <param name="mongoClient">The singleton MongoClient instance injected via DI.</param>
        /// <param name="logger">Logger for structured logging.</param>
        /// <param name="config">Application configuration including sensitive settings.</param>
        public CoinService(
            IOptions<MongoDbSettings> mongoDbSettings,
            IMongoClient mongoClient,
            ILogger<CoinService> logger)
            : base(mongoClient, mongoDbSettings, "coins", logger)
        {
        }

        /// <summary>
        /// Asynchronously retrieves crypto data for a given symbol.
        /// </summary>
        /// <param name="symbol">The trading pair symbol (e.g., BTCUSDT).</param>
        /// <returns>A <see cref="CoinData"/> object with cryptocurrency information, or null if not found.</returns>
        public async Task<CoinData?> GetCryptoFromSymbolAsync(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                _logger.LogError("Argument 'symbol' cannot be null or empty.");
                return null;
            }
            try
            {
                // Example filter: check if the symbol starts with the coin's ticker (case-insensitive)
                var filter = Builders<CoinData>.Filter.Where(coin =>
                    symbol.StartsWith(coin.Ticker, StringComparison.OrdinalIgnoreCase));
                var coin = await _collection.Find(filter).FirstOrDefaultAsync();
                if (coin == null)
                {
                    _logger.LogWarning("No crypto data found for symbol: {Symbol}", symbol);
                    return null;
                }
                return coin;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to fetch crypto for symbol: {Symbol}", symbol);
                return null;
            }
        }
    }
}
