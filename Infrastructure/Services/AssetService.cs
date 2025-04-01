using Application.Interfaces;
using Domain.DTOs;
using Domain.DTOs.Settings;
using Domain.Exceptions;
using Domain.Models.Asset;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Infrastructure.Services
{
    public class AssetService : BaseService<AssetData>, IAssetService
    {
        private const string CACHE_KEY_ASSET_TICKER = "asset_ticker:{0}";
        private const string CACHE_KEY_ASSET_SYMBOL = "asset_symbol:{0}";
        private const string CACHE_KEY_SUPPORTED_TICKERS = "supported_tickers";

        /// <summary>
        /// Initializes a new instance of the <see cref="AssetService"/> class.
        /// </summary>
        public AssetService(
            IOptions<MongoDbSettings> mongoDbSettings,
            IMongoClient mongoClient,
            ILogger<AssetService> logger,
            IMemoryCache cache
            )
            : base(
                  mongoClient,
                  mongoDbSettings,
                  "assets",
                  logger,
                  cache,
                  new List<CreateIndexModel<AssetData>>
                  {
                      new CreateIndexModel<AssetData>(
                          Builders<AssetData>.IndexKeys.Ascending(x => x.Ticker),
                          new CreateIndexOptions { Name = "Ticker_1", Unique = true }
                      ),
                      new CreateIndexModel<AssetData>(
                          Builders<AssetData>.IndexKeys.Ascending(x => x.Symbol),
                          new CreateIndexOptions { Name = "Symbol_1" }
                      ),
                      new CreateIndexModel<AssetData>(
                          Builders<AssetData>.IndexKeys.Ascending(x => x.Type),
                          new CreateIndexOptions { Name = "Class_1" }
                      )
                  }
                  )
        {
            // Initialize essential assets if needed
            Task.Run(() => InitializeEssentialAssets());
        }

        private async Task InitializeEssentialAssets()
        {
            try
            {
                var btcAsset = await GetOneAsync(Builders<AssetData>.Filter.Eq(o => o.Ticker, "BTC"));
                if (btcAsset is null)
                {
                    await InsertOneAsync(new()
                    {
                        Name = "Bitcoin",
                        Ticker = "BTC",
                        Precision = 18,
                        Symbol = "₿",
                        Exchange = "Binance"
                    });
                    _logger.LogInformation("Created initial Bitcoin asset");
                }

                var usdtAsset = await GetOneAsync(Builders<AssetData>.Filter.Eq(o => o.Ticker, "USDT"));
                if (usdtAsset is null)
                {
                    await InsertOneAsync(new()
                    {
                        Name = "Tether USD",
                        Ticker = "USDT",
                        Precision = 6,
                        Symbol = "₮",
                        Exchange = "Binance",
                        Type = Domain.Constants.AssetType.Exchange
                    });
                    _logger.LogInformation("Created initial USDT asset");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing essential assets");
            }
        }

        /// <summary>
        /// Asynchronously retrieves crypto data for a given symbol.
        /// </summary>
        public async Task<ResultWrapper<AssetData>> GetFromSymbolAsync(string symbol)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(symbol))
                {
                    _logger.LogError("Argument 'symbol' cannot be null or empty.");
                    throw new ArgumentNullException(nameof(symbol));
                }

                string cacheKey = string.Format(CACHE_KEY_ASSET_SYMBOL, symbol.ToLowerInvariant());

                return await GetOrCreateCachedItemAsync<ResultWrapper<AssetData>>(
                    cacheKey,
                    async () =>
                    {
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
                    },
                    TimeSpan.FromMinutes(30) // Assets change rarely, so longer cache
                );
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
        public async Task<ResultWrapper<AssetData>> GetByTickerAsync(string ticker)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(ticker))
                {
                    _logger.LogError("Argument 'ticker' cannot be null or empty.");
                    throw new ArgumentNullException(nameof(ticker));
                }

                string cacheKey = string.Format(CACHE_KEY_ASSET_TICKER, ticker.ToLowerInvariant());

                return await GetOrCreateCachedItemAsync<ResultWrapper<AssetData>>(
                    cacheKey,
                    async () =>
                    {
                        var filter = Builders<AssetData>.Filter.Where(asset =>
                            asset.Ticker.Equals(ticker, StringComparison.OrdinalIgnoreCase));

                        var asset = await GetOneAsync(filter);
                        if (asset == null)
                        {
                            throw new ResourceNotFoundException("Asset", ticker);
                        }

                        return ResultWrapper<AssetData>.Success(asset);
                    },
                    TimeSpan.FromMinutes(30) // Assets change rarely, so longer cache
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to fetch crypto for ticker: {Ticker}", ticker);
                return ResultWrapper<AssetData>.FromException(ex);
            }
        }

        /// <summary>
        /// Returns a list of supported asset tickers.
        /// </summary>
        public async Task<ResultWrapper<IEnumerable<string>>> GetSupportedTickersAsync()
        {
            try
            {
                return await GetOrCreateCachedItemAsync<ResultWrapper<IEnumerable<string>>>(
                    CACHE_KEY_SUPPORTED_TICKERS,
                    async () =>
                    {
                        var filter = Builders<AssetData>.Filter.Empty;
                        var assets = await GetAllAsync(filter);

                        if (assets == null || !assets.Any())
                        {
                            throw new KeyNotFoundException($"No crypto data found.");
                        }

                        return ResultWrapper<IEnumerable<string>>.Success(assets.Select(a => a.Ticker));
                    },
                    TimeSpan.FromMinutes(30) // Long cache as this rarely changes
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to fetch supported assets");
                return ResultWrapper<IEnumerable<string>>.FromException(ex);
            }
        }

        // Override InsertOneAsync to invalidate caches
        public override async Task<InsertResult> InsertOneAsync(
            AssetData entity,
            IClientSessionHandle? session = null,
            CancellationToken cancellationToken = default)
        {
            var result = await base.InsertOneAsync(entity, session, cancellationToken);

            if (result.IsAcknowledged && result.InsertedId.HasValue)
            {
                // Invalidate specific caches
                _cache.Remove(string.Format(CACHE_KEY_ASSET_TICKER, entity.Ticker.ToLowerInvariant()));
                _cache.Remove(string.Format(CACHE_KEY_ASSET_SYMBOL, entity.Symbol.ToLowerInvariant()));
                _cache.Remove(CACHE_KEY_SUPPORTED_TICKERS);

                _logger.LogDebug("Invalidated asset caches after insert: {Ticker}", entity.Ticker);
            }

            return result;
        }

        // Override UpdateOneAsync to invalidate caches
        public override async Task<UpdateResult> UpdateOneAsync(
            Guid id,
            object updatedFields,
            IClientSessionHandle? session = null,
            CancellationToken cancellationToken = default)
        {
            // Get the asset before update to know which caches to invalidate
            var asset = await GetByIdAsync(id);
            var result = await base.UpdateOneAsync(id, updatedFields, session, cancellationToken);

            if (result.ModifiedCount > 0 && asset != null)
            {
                // Invalidate specific caches
                _cache.Remove(string.Format(CACHE_KEY_ASSET_TICKER, asset.Ticker.ToLowerInvariant()));
                _cache.Remove(string.Format(CACHE_KEY_ASSET_SYMBOL, asset.Symbol.ToLowerInvariant()));
                _cache.Remove(CACHE_KEY_SUPPORTED_TICKERS);

                _logger.LogDebug("Invalidated asset caches after update: {Ticker}", asset.Ticker);
            }

            return result;
        }
    }
}