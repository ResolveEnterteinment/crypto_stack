using Application.Interfaces;
using Domain.DTOs;
using Domain.Models.Crypto;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Infrastructure.Services
{
    public class AssetService : BaseService<AssetData>, IAssetService
    {

        /// <summary>
        /// Initializes a new instance of the <see cref="AssetService"/> class.
        /// </summary>
        /// <param name="mongoDbSettings">MongoDB settings injected via IOptions.</param>
        /// <param name="mongoClient">The singleton MongoClient instance injected via DI.</param>
        /// <param name="logger">Logger for structured logging.</param>
        /// <param name="config">Application configuration including sensitive settings.</param>
        public AssetService(
            IOptions<MongoDbSettings> mongoDbSettings,
            IMongoClient mongoClient,
            ILogger<AssetService> logger)
            : base(mongoClient, mongoDbSettings, "assets", logger)
        {
            Init();
        }

        private async void Init()
        {
            var btcAsset = await GetOneAsync(new FilterDefinitionBuilder<AssetData>().Eq(o => o.Ticker, "BTC"));
            if (btcAsset is null) await InsertOneAsync(new()
            {
                Name = "Bitcoin",
                Ticker = "BTC",
                Precision = 18,
                Symbol = "₿"
            });
        }

        /// <summary>
        /// Asynchronously retrieves crypto data for a given symbol.
        /// </summary>
        /// <param name="symbol">The trading pair symbol (e.g., BTCUSDT).</param>
        /// <returns>A <see cref="AssetData"/> object with cryptocurrency information, or null if not found.</returns>
        public async Task<ResultWrapper<AssetData>> GetFromSymbolAsync(string symbol)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(symbol))
                {
                    _logger.LogError("Argument 'symbol' cannot be null or empty.");
                    throw new ArgumentNullException(nameof(symbol));
                }
                // Example filter: check if the symbol starts with the coin's ticker (case-insensitive)
                var filter = Builders<AssetData>.Filter.Where(asset =>
                    symbol.StartsWith(asset.Ticker, StringComparison.OrdinalIgnoreCase));
                var asset = await GetOneAsync(filter);
                if (asset == null)
                {
                    _logger.LogWarning("No crypto data found for symbol: {Symbol}", symbol);
                    throw new KeyNotFoundException($"No crypto data found for symbol: {symbol}");
                }
                return ResultWrapper<AssetData>.Success(asset);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to fetch crypto for symbol: {Symbol}", symbol);
                return ResultWrapper<AssetData>.FromException(ex);
            }
        }

        /// <summary>
        /// Asynchronously retrieves asset data for a given ticker.
        /// </summary>
        /// <param name="ticker">The asset ticker (e.g., BTC, USDT).</param>
        /// <returns>A <see cref="AssetData"/> object or null if not found.</returns>
        public async Task<ResultWrapper<AssetData>> GetByTickerAsync(string ticker)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(ticker))
                {
                    _logger.LogError("Argument 'ticker' cannot be null or empty.");
                    throw new ArgumentNullException(nameof(ticker));
                }
                var filter = Builders<AssetData>.Filter.Where(asset =>
                    asset.Ticker.Equals(ticker, StringComparison.OrdinalIgnoreCase));

                //var filter = Builders<AssetData>.Filter.Eq(asset => asset.Ticker, ticker);
                var asset = await GetOneAsync(filter);
                if (asset == null)
                {
                    throw new KeyNotFoundException($"No crypto data found for symbol: {ticker}");
                }
                return ResultWrapper<AssetData>.Success(asset);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to fetch crypto for symbol: {Symbol}", ticker);
                return ResultWrapper<AssetData>.FromException(ex);
            }
        }

        /// <summary>
        /// Returns a list of assets data.
        /// </summary>
        /// <returns>An enumerable list of <see cref="AssetData"/> object.</returns>
        public async Task<ResultWrapper<IEnumerable<string>>> GetSupportedTickersAsync()
        {
            try
            {
                var filter = Builders<AssetData>.Filter.Empty;
                var assets = await GetAllAsync(filter);
                if (assets == null)
                {
                    throw new KeyNotFoundException($"No crypto data found.");
                }
                return ResultWrapper<IEnumerable<string>>.Success(assets.Select(a => a.Ticker));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to fetch supported assets");
                return ResultWrapper<IEnumerable<string>>.FromException(ex);
            }
        }
    }
}
