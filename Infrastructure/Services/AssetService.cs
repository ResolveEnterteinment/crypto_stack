using Application.Contracts.Requests.Asset;
using Application.Interfaces;
using Domain.Constants;
using Domain.DTOs;
using Domain.DTOs.Asset;
using Domain.DTOs.Settings;
using Domain.Exceptions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using AssetData = Domain.Models.Asset.AssetData;

namespace Infrastructure.Services
{
    public class AssetService : BaseService<Domain.Models.Asset.AssetData>, IAssetService
    {
        private const string CACHE_KEY_ASSET_TICKER = "asset_ticker:{0}";
        private const string CACHE_KEY_ASSET_SYMBOL = "asset_symbol:{0}";
        private const string CACHE_KEY_SUPPORTED_TICKERS = "supported_tickers";
        private const string CACHE_KEY_SUPPORTED_ASSETS = "supported_assets";
        private const string CACHE_KEY_ALL_ASSETS = "all_assets";

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
                  new List<CreateIndexModel<Domain.Models.Asset.AssetData>>
                  {
                      new CreateIndexModel<Domain.Models.Asset.AssetData>(
                          Builders<Domain.Models.Asset.AssetData>.IndexKeys.Ascending(x => x.Ticker),
                          new CreateIndexOptions { Name = "Ticker_1", Unique = true }
                      ),
                      new CreateIndexModel<Domain.Models.Asset.AssetData>(
                          Builders<Domain.Models.Asset.AssetData>.IndexKeys.Ascending(x => x.Symbol),
                          new CreateIndexOptions { Name = "Symbol_1" }
                      ),
                      new CreateIndexModel<Domain.Models.Asset.AssetData>(
                          Builders<Domain.Models.Asset.AssetData>.IndexKeys.Ascending(x => x.Type),
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
                var btcAsset = await base.GetOneAsync(Builders<Domain.Models.Asset.AssetData>.Filter.Eq(o => o.Ticker, "BTC"));
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

                var usdtAsset = await base.GetOneAsync(Builders<Domain.Models.Asset.AssetData>.Filter.Eq(o => o.Ticker, "USDT"));
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
        /// Asynchronously retrieves asset data for a given symbol.
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

                return await GetOrCreateCachedItemAsync(
                    cacheKey,
                    async () =>
                    {
                        // Example filter: check if the symbol starts with the coin's ticker (case-insensitive)
                        var filter = Builders<AssetData>.Filter.Where(asset =>
                            symbol.StartsWith(asset.Ticker, StringComparison.OrdinalIgnoreCase));

                        var asset = await base.GetOneAsync(filter);
                        if (asset == null)
                        {
                            _logger.LogWarning("No asset data found for symbol: {Symbol}", symbol);
                            throw new KeyNotFoundException($"No asset data found for symbol: {symbol}");
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

                return await GetOrCreateCachedItemAsync(
                    cacheKey,
                    async () =>
                    {
                        var filter = Builders<Domain.Models.Asset.AssetData>.Filter.Where(asset =>
                            asset.Ticker.Equals(ticker, StringComparison.OrdinalIgnoreCase));

                        var asset = await base.GetOneAsync(filter);
                        if (asset == null)
                        {
                            throw new ResourceNotFoundException("Asset", ticker);
                        }

                        return ResultWrapper<Domain.Models.Asset.AssetData>.Success(asset);
                    },
                    TimeSpan.FromMinutes(30) // Assets change rarely, so longer cache
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to fetch crypto for ticker: {Ticker}", ticker);
                return ResultWrapper<Domain.Models.Asset.AssetData>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<Guid>> CreateAsync(AssetCreateRequest request)
        {
            try
            {
                #region Validate
                if (request == null)
                {
                    throw new ArgumentNullException("Asset create request cannot be null.");
                }

                if (string.IsNullOrWhiteSpace(request.Name))
                {
                    throw new ArgumentException($"Invalid asset name: {request.Name}");
                }

                if (string.IsNullOrWhiteSpace(request.Ticker))
                {
                    throw new ArgumentException($"Invalid asset ticker: {request.Ticker}");
                }

                if (string.IsNullOrWhiteSpace(request.Exchange))
                {
                    throw new ArgumentException($"Invalid asset exchange: {request.Exchange}");
                }

                if (string.IsNullOrWhiteSpace(request.Type) || !AssetType.AllValues.Contains(request.Type))
                {
                    throw new ArgumentException($"Invalid asset type: {request.Type}");
                }
                #endregion Validate

                var assetData = new AssetData
                {
                    Name = request.Name,
                    Ticker = request.Ticker,
                    Symbol = request.Symbol,
                    Precision = request.Precision,
                    SubunitName = request.SubunitName,
                    Exchange = request.Exchange,
                    Type = request.Type
                };

                var result = await InsertOneAsync(assetData);
                if (!result.IsAcknowledged)
                {
                    throw new MongoException("Failed to insert subscription into database.");
                }

                var insertedId = result.InsertedId!.Value;

                _logger.LogInformation("Successfully inserted asset {AssetId}", insertedId);

                // Invalidate user subscriptions cache
                _cache.Remove(string.Format(CACHE_KEY_SUPPORTED_ASSETS, insertedId));

                return ResultWrapper<Guid>.Success(result.InsertedId.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to process asset create request: {ex.Message}");
                return ResultWrapper<Guid>.FromException(ex);
            }
        }

        public async Task<ResultWrapper> UpdateAsync(Guid id, AssetUpdateRequest request)
        {
            #region Validate

            if (request == null)
            {
                throw new ArgumentNullException("Asset create request cannot be null.");
            }

            if (request.Precision != null && request.Precision <= 0)
            {
                throw new ArgumentException($"Invalid asset precision: {request.Precision}");
            }

            if (!string.IsNullOrWhiteSpace(request.Symbol) && request.Symbol.Length > 1)
            {
                throw new ArgumentException($"Asset symbol must be 1 character: {request.Symbol}");
            }

            if (!string.IsNullOrWhiteSpace(request.Type) && !AssetType.AllValues.Contains(request.Type))
            {
                throw new ArgumentException($"Invalid asset type: {request.Type}");
            }

            #endregion Validate

            try
            {
                // Get the asset before updating to know the user ID for cache invalidation
                var asset = await GetByIdAsync(id);
                if (asset == null)
                {
                    throw new KeyNotFoundException($"Subscription with ID {id} not found");
                }

                var updateFields = new Dictionary<string, object>();

                // Only include non-null fields in the update
                if (!string.IsNullOrEmpty(request.Name))
                {
                    updateFields["Name"] = request.Name;
                }

                if (!string.IsNullOrEmpty(request.Ticker))
                {
                    updateFields["Ticker"] = request.Ticker;
                }

                if (!string.IsNullOrEmpty(request.Symbol))
                {
                    updateFields["Symbol"] = request.Symbol;
                }

                if (request.Precision.HasValue)
                {
                    updateFields["Precision"] = request.Precision.Value;
                }

                if (!string.IsNullOrEmpty(request.SubunitName))
                {
                    updateFields["SubunitName"] = request.SubunitName;
                }

                if (!string.IsNullOrEmpty(request.Exchange))
                {
                    updateFields["Exchange"] = request.Exchange;
                }

                if (!string.IsNullOrEmpty(request.Type))
                {
                    updateFields["Type"] = request.Type;
                }

                var updateResult = await UpdateOneAsync(id, updateFields);

                if (updateResult.IsAcknowledged && updateResult.ModifiedCount > 0)
                {
                    _logger.LogInformation($"Successfully updated asset {id}");
                }

                return ResultWrapper.Success($"Successfully updated asset {id}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update asset {SubscriptionId}: {Message}", id, ex.Message);
                return ResultWrapper.FromException(ex);
            }
        }

        /// <summary>
        /// Returns a list of supported asset tickers.
        /// </summary>
        public async Task<ResultWrapper<IEnumerable<AssetDto>>> GetSupportedAssetsAsync()
        {
            try
            {
                return await GetOrCreateCachedItemAsync(
                    CACHE_KEY_SUPPORTED_ASSETS,
                    async () =>
                    {
                        var filter = Builders<AssetData>.Filter.Eq(a => a.Type, AssetType.Exchange);
                        var assets = await GetAllAsync(filter);

                        if (assets == null || !assets.Any())
                        {
                            throw new KeyNotFoundException($"No asset data found.");
                        }
                        var result = assets.Select(a => new AssetDto
                        {
                            Name = a.Name,
                            Ticker = a.Ticker,
                            Symbol = a.Symbol,
                            Precision = a.Precision,
                            SubunitName = a.SubunitName,
                        });
                        return ResultWrapper<IEnumerable<AssetDto>>.Success(result);
                    },
                    TimeSpan.FromMinutes(30) // Long cache as this rarely changes
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to fetch supported assets");
                return ResultWrapper<IEnumerable<AssetDto>>.FromException(ex);
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
                        var filter = Builders<Domain.Models.Asset.AssetData>.Filter.Empty;
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
                _cache.Remove(CACHE_KEY_SUPPORTED_ASSETS);

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
                _cache.Remove(CACHE_KEY_SUPPORTED_ASSETS);

                _logger.LogDebug("Invalidated asset caches after update: {Ticker}", asset.Ticker);
            }

            return result;
        }
    }
}