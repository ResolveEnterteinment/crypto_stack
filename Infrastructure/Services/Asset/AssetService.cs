using Application.Contracts.Requests.Asset;
using Application.Interfaces.Asset;
using Domain.Constants;
using Domain.Constants.Asset;
using Domain.Constants.Logging;
using Domain.DTOs;
using Domain.DTOs.Asset;
using Domain.DTOs.Logging;
using Domain.Exceptions;
using Infrastructure.Services.Base;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using AssetData = Domain.Models.Asset.AssetData;

namespace Infrastructure.Services.Asset
{
    public class AssetService : BaseService<AssetData>, IAssetService
    {
        private const string CACHE_KEY_ASSET_TICKER = "asset_ticker:{0}";
        private const string CACHE_KEY_ASSET_SYMBOL = "asset_symbol:{0}";
        private const string CACHE_KEY_SUPPORTED_TICKERS = "supported_tickers";
        private const string CACHE_KEY_SUPPORTED_ASSETS = "supported_assets";
        private const string CACHE_KEY_ASSET_MANY_TICKERS = "asset_many_tickers:{0}";

        // Cache durations
        private static readonly TimeSpan ASSET_CACHE_DURATION = TimeSpan.FromDays(1);
        private static readonly TimeSpan SUPPORTED_ASSETS_CACHE_DURATION = TimeSpan.FromDays(1);
        private static readonly TimeSpan SUPPORTED_TICKERS_CACHE_DURATION = TimeSpan.FromDays(1);

        public AssetService(IServiceProvider serviceProvider)
            : base(
                serviceProvider,
                new()
                {
                    IndexModels = [
                    new CreateIndexModel<AssetData>(
                        Builders<AssetData>.IndexKeys.Ascending(x => x.Ticker),
                        new CreateIndexOptions { Name = "Ticker_1", Unique = true }),
                    new CreateIndexModel<AssetData>(
                        Builders<AssetData>.IndexKeys.Ascending(x => x.Symbol),
                        new CreateIndexOptions { Name = "Symbol_1" }),
                    new CreateIndexModel<AssetData>(
                        Builders<AssetData>.IndexKeys.Ascending(x => x.Type),
                        new CreateIndexOptions { Name = "Type_1" }),
                    new CreateIndexModel<AssetData>(
                        Builders<AssetData>.IndexKeys.Ascending(x => x.Class),
                        new CreateIndexOptions { Name = "Class_1" })
                    ]
                })
        {
            InitializeEssentialAssetsAsync();
        }

        private async Task InitializeEssentialAssetsAsync()
        {
            await _resilienceService.CreateBuilder<bool>(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Asset",
                    FileName = "AssetService",
                    OperationName = "InitializeEssentialAssetsAsync()",
                    State = [],
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    if (await GetByTickerAsync("BTC") == null)
                    {
                        await InsertAsync(new AssetData { Name = "Bitcoin", Ticker = "BTC", Precision = 18, Symbol = "₿", Exchange = "Binance", Type = AssetType.Exchange, Class = AssetClass.Crypto });
                    }

                    if (await GetByTickerAsync("USDT") == null)
                    {
                        await InsertAsync(new AssetData { Name = "Tether USD", Ticker = "USDT", Precision = 6, Symbol = "₮", Exchange = "Binance", Type = AssetType.Exchange, Class = AssetClass.Stablecoin });
                    }
                    return true;
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<Guid>> CreateAsync(AssetCreateRequest req)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Asset",
                    FileName = "AssetService",
                    OperationName = "CreateAsync(AssetCreateRequest req)",
                    State = {
                        ["AssetCreateRequest"] = req,
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    var asset = new AssetData
                    {
                        Name = req.Name,
                        Ticker = req.Ticker.ToUpper(),
                        Symbol = req.Symbol,
                        Precision = req.Precision,
                        SubunitName = req.SubunitName,
                        Exchange = req.Exchange,
                        Type = req.Type,
                        Class = req.Class
                    };

                    var insertResult = await InsertAsync(asset);

                    if (insertResult is null || !insertResult.IsSuccess)
                    {
                        throw new DatabaseException(insertResult?.ErrorMessage ?? "Insert result returned null");
                    }

                    var insertedAsset = insertResult.Data;
                    var assetId = insertedAsset.AffectedIds.ToList()[0];

                    // Invalidate related caches after creation
                    await InvalidateRelatedCachesAsync(req.Ticker.ToUpper(), req.Symbol);

                    return assetId;
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<bool>> UpdateAsync(Guid id, AssetUpdateRequest req)
        {
            return await _resilienceService.CreateBuilder<bool>(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Asset",
                    FileName = "AssetService",
                    OperationName = "UpdateAsync(Guid id, AssetUpdateRequest req)",
                    State = {
                        ["AssetUpdateRequest"] = req,
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    // Get the existing asset to determine which caches to invalidate
                    var existingAsset = await GetByIdAsync(id);
                    string? existingTicker = existingAsset?.Data?.Ticker;
                    string? existingSymbol = existingAsset?.Data?.Symbol;

                    var fields = new Dictionary<string, object>();
                    if (!string.IsNullOrWhiteSpace(req.Name))
                    {
                        fields["Name"] = req.Name;
                    }

                    if (!string.IsNullOrWhiteSpace(req.Ticker))
                    {
                        fields["Ticker"] = req.Ticker;
                    }

                    if (!string.IsNullOrWhiteSpace(req.Symbol))
                    {
                        fields["Symbol"] = req.Symbol;
                    }

                    if (req.Precision.HasValue)
                    {
                        fields["Precision"] = req.Precision.Value;
                    }

                    if (!string.IsNullOrWhiteSpace(req.SubunitName))
                    {
                        fields["SubunitName"] = req.SubunitName;
                    }

                    if (!string.IsNullOrWhiteSpace(req.Exchange))
                    {
                        fields["Exchange"] = req.Exchange;
                    }

                    if (!string.IsNullOrWhiteSpace(req.Type))
                    {
                        fields["Type"] = req.Type;
                    }

                    var updatedResult = await UpdateAsync(id, fields);

                    if (updatedResult is null || !updatedResult.IsSuccess || updatedResult.Data.ModifiedCount == 0)
                    {
                        throw new DatabaseException(updatedResult?.ErrorMessage ?? "Update result returned null");
                    }

                    // Invalidate caches for both old and new values
                    await InvalidateRelatedCachesAsync(existingTicker, existingSymbol);
                    await InvalidateRelatedCachesAsync(req.Ticker, req.Symbol);

                    return true;
                }
            )
                .ExecuteAsync();
        }

        /// <summary>
        /// Gets multiple assets by their ticker symbols with caching
        /// </summary>
        /// <param name="tickers">Collection of ticker symbols to search for</param>
        /// <returns>List of matching assets</returns>
        public async Task<ResultWrapper<List<AssetData>>> GetManyByTickersAsync(IEnumerable<string> tickers)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Asset",
                    FileName = "AssetService",
                    OperationName = "GetManyByTickersAsync(IEnumerable<string> tickers)",
                    State = {
                        ["Tickers"] = tickers,
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    if (tickers == null || !tickers.Any())
                    {
                        throw new ValidationException("Tickers is required", []);
                    }

                    // Convert to array and normalize tickers (uppercase, trim, remove duplicates)
                    var normalizedTickers = tickers
                        .Where(t => !string.IsNullOrWhiteSpace(t))
                        .Select(t => t.Trim().ToUpperInvariant())
                        .Distinct()
                        .ToArray();

                    if (normalizedTickers.Length == 0)
                    {
                        return [];
                    }

                    // Create cache key based on sorted tickers for consistent caching
                    var sortedTickers = normalizedTickers.OrderBy(t => t).ToArray();
                    var cacheKey = string.Format(CACHE_KEY_ASSET_MANY_TICKERS, string.Join(",", sortedTickers));

                    var cachedAssets = await _cacheService.GetAnyCachedAsync<List<AssetData>>(
                        cacheKey,
                        async () =>
                        {
                            var filter = Builders<AssetData>.Filter.In(a => a.Ticker, normalizedTickers);
                            var result = await GetManyAsync(filter);
                            if (result == null || !result.IsSuccess)
                                throw new AssetFetchException(result?.ErrorMessage ?? "Asset fetch result returned null");
                            return result.Data;
                        },
                        ASSET_CACHE_DURATION
                    );

                    return cachedAssets ?? [];
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<AssetData>> GetByTickerAsync(string ticker)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Asset",
                    FileName = "AssetService",
                    OperationName = "GetByTickerAsync(string ticker)",
                    State = {
                        ["Ticker"] = ticker,
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    if (string.IsNullOrWhiteSpace(ticker))
                    {
                        throw new ValidationException("Ticker is required", new() { ["ticker"] = ["Ticker cannot be null or empty"] });
                    }

                    var normalizedTicker = ticker.Trim().ToUpperInvariant();
                    var cacheKey = string.Format(CACHE_KEY_ASSET_TICKER, normalizedTicker);

                    var cachedAsset = await _cacheService.GetAnyCachedAsync<AssetData>(
                        cacheKey,
                        async () =>
                        {
                            var filter = Builders<AssetData>.Filter.Where(a => a.Ticker.Equals(normalizedTicker, StringComparison.OrdinalIgnoreCase));
                            var result = await GetOneAsync(filter);
                            if (result == null || !result.IsSuccess)
                                throw new AssetFetchException(result?.ErrorMessage ?? "Asset fetch result returned null");
                            return result.Data;
                        },
                        ASSET_CACHE_DURATION
                    );

                    if (cachedAsset == null)
                    {
                        throw new AssetFetchException($"Asset with ticker '{ticker}' not found");
                    }

                    return cachedAsset;
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<AssetData>> GetFromSymbolAsync(string symbol)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Asset",
                    FileName = "AssetService",
                    OperationName = "GetFromSymbolAsync(string symbol)",
                    State = {
                        ["Symbol"] = symbol,
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    if (string.IsNullOrWhiteSpace(symbol))
                    {
                        throw new ValidationException("Symbol is required", new() { ["symbol"] = ["Symbol cannot be null or empty"] });
                    }

                    var cacheKey = string.Format(CACHE_KEY_ASSET_SYMBOL, symbol);

                    var cachedAsset = await _cacheService.GetAnyCachedAsync<AssetData>(
                        cacheKey,
                        async () =>
                        {
                            var filter = Builders<AssetData>.Filter.Where(a => symbol.StartsWith(a.Symbol, StringComparison.OrdinalIgnoreCase));
                            var result = await GetOneAsync(filter);
                            if (result == null || !result.IsSuccess)
                                throw new AssetFetchException(result?.ErrorMessage ?? "Asset fetch result returned null");
                            return result.Data;
                        },
                        ASSET_CACHE_DURATION
                    );

                    if (cachedAsset == null)
                    {
                        throw new AssetFetchException($"Asset with symbol '{symbol}' not found");
                    }

                    return cachedAsset;
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<IEnumerable<AssetDto>>> GetSupportedAssetsAsync()
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Asset",
                    FileName = "AssetService",
                    OperationName = "GetSupportedAssetsAsync()",
                    State = [],
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    var cachedAssets = await _cacheService.GetAnyCachedAsync<IEnumerable<AssetDto>>(
                        CACHE_KEY_SUPPORTED_ASSETS,
                        async () =>
                        {
                            var result = await _repository.GetAllAsync(
                                 Builders<AssetData>.Filter.And([
                                     Builders<AssetData>.Filter.Eq(a => a.Type, AssetType.Exchange),
                                     Builders<AssetData>.Filter.Eq(a => a.Class, AssetClass.Crypto)
                                 ]))
                            .ContinueWith(t => t.Result?.Select(a => new AssetDto(a)));

                            if (result == null)
                                throw new AssetFetchException("Failed to fetch supported assets.");

                            return result;
                        },
                        SUPPORTED_ASSETS_CACHE_DURATION
                    );

                    return cachedAssets ?? Enumerable.Empty<AssetDto>();
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<IEnumerable<string>>> GetSupportedTickersAsync()
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Asset",
                    FileName = "AssetService",
                    OperationName = "GetSupportedTickersAsync()",
                    State = [],
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    var cachedTickers = await _cacheService.GetAnyCachedAsync<IEnumerable<string>>(
                        CACHE_KEY_SUPPORTED_TICKERS,
                        async () =>
                        {
                            var result = await _repository.GetAllAsync().ContinueWith(t => t.Result?.Select(a => a.Ticker));

                            if (result == null)
                                throw new AssetFetchException("Failed to fetch supported tickers.");

                            return result;
                        },
                        SUPPORTED_TICKERS_CACHE_DURATION
                    );

                    return cachedTickers ?? Enumerable.Empty<string>();
                })
                .ExecuteAsync();
        }

        /// <summary>
        /// Invalidates all related caches for an asset
        /// </summary>
        /// <param name="ticker">The asset ticker</param>
        /// <param name="symbol">The asset symbol</param>
        private async Task InvalidateRelatedCachesAsync(string? ticker, string? symbol)
        {
            try
            {
                var cacheKeysToInvalidate = new List<string>
                {
                    CACHE_KEY_SUPPORTED_ASSETS,
                    CACHE_KEY_SUPPORTED_TICKERS
                };

                if (!string.IsNullOrWhiteSpace(ticker))
                {
                    var normalizedTicker = ticker.Trim().ToUpperInvariant();
                    cacheKeysToInvalidate.Add(string.Format(CACHE_KEY_ASSET_TICKER, normalizedTicker));
                }

                if (!string.IsNullOrWhiteSpace(symbol))
                {
                    cacheKeysToInvalidate.Add(string.Format(CACHE_KEY_ASSET_SYMBOL, symbol));
                }

                // Invalidate entity cache by ID (requires a database lookup to get the asset)
                if (!string.IsNullOrWhiteSpace(ticker))
                {
                    try
                    {
                        var asset = await GetByTickerAsync(ticker);
                        if (asset?.Data?.Id != null)
                        {
                            var entityCacheKey = _cacheService.GetCacheKey(asset.Data.Id);
                            cacheKeysToInvalidate.Add(entityCacheKey);
                        }
                    }
                    catch (Exception ex)
                    {
                        _loggingService.LogWarning("Failed to invalidate entity cache for ticker {Ticker}: {Error}", ticker, ex.Message);
                    }
                }

                // Invalidate all many-tickers cache keys (this is a simplification - in a real scenario you'd track these)
                // For now, we'll invalidate the main collection cache
                cacheKeysToInvalidate.Add(_cacheService.GetCollectionCacheKey());

                foreach (var cacheKey in cacheKeysToInvalidate)
                {
                    _cacheService.Invalidate(cacheKey);
                }

                _loggingService.LogInformation("Invalidated {Count} cache keys for asset ticker: {Ticker}, symbol: {Symbol}",
                    cacheKeysToInvalidate.Count, ticker, symbol);
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Failed to invalidate asset caches for ticker {Ticker}, symbol {Symbol}: {Error}",
                    ticker, symbol, ex.Message);
            }
        }

        /// <summary>
        /// Invalidates asset caches for multiple tickers efficiently
        /// </summary>
        /// <param name="tickers">Collection of tickers to invalidate</param>
        public async Task InvalidateAssetCachesAsync(IEnumerable<string> tickers)
        {
            if (tickers == null || !tickers.Any())
                return;

            try
            {
                foreach (var ticker in tickers)
                {
                    await InvalidateRelatedCachesAsync(ticker, null);
                }

                _loggingService.LogInformation("Invalidated asset caches for {Count} tickers", tickers.Count());
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Failed to invalidate asset caches for multiple tickers: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Warms up the cache for essential assets
        /// </summary>
        public async Task<ResultWrapper> WarmupEssentialAssetCacheAsync()
        {
            try
            {
                _loggingService.LogInformation("Warming up essential asset caches");

                // Pre-load essential assets
                var essentialTickers = new[] { "BTC", "USDT", "ETH", "BNB" };
                var warmupTasks = essentialTickers.Select(GetByTickerAsync).ToArray();

                // Pre-load supported assets and tickers
                var supportedAssetsTask = GetSupportedAssetsAsync();
                var supportedTickersTask = GetSupportedTickersAsync();

                // Wait for all warmup tasks
                await Task.WhenAll(warmupTasks.Concat(new Task[] { supportedAssetsTask, supportedTickersTask }));

                _loggingService.LogInformation("Successfully warmed up essential asset caches");
                return ResultWrapper.Success("Essential asset caches warmed up successfully");
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Error warming up essential asset caches: {Error}", ex.Message);
                return ResultWrapper.FromException(ex);
            }
        }

        /// <summary>
        /// Gets cache statistics for monitoring
        /// </summary>
        public async Task<ResultWrapper<AssetCacheStats>> GetCacheStatsAsync()
        {
            try
            {
                var stats = new AssetCacheStats
                {
                    SupportedAssetsExists = _cacheService.TryGetValue<IEnumerable<AssetDto>>(CACHE_KEY_SUPPORTED_ASSETS, out _),
                    SupportedTickersExists = _cacheService.TryGetValue<IEnumerable<string>>(CACHE_KEY_SUPPORTED_TICKERS, out _),
                    Timestamp = DateTime.UtcNow
                };

                // Check some common asset caches
                var commonTickers = new[] { "BTC", "USDT", "ETH" };
                stats.CommonAssetCacheHits = 0;
                foreach (var ticker in commonTickers)
                {
                    var cacheKey = string.Format(CACHE_KEY_ASSET_TICKER, ticker);
                    if (_cacheService.TryGetValue<AssetData>(cacheKey, out _))
                    {
                        stats.CommonAssetCacheHits++;
                    }
                }

                return ResultWrapper<AssetCacheStats>.Success(stats);
            }
            catch (Exception ex)
            {
                return ResultWrapper<AssetCacheStats>.FromException(ex);
            }
        }
    }

    /// <summary>
    /// Cache statistics for monitoring asset cache health
    /// </summary>
    public class AssetCacheStats
    {
        public bool SupportedAssetsExists { get; set; }
        public bool SupportedTickersExists { get; set; }
        public int CommonAssetCacheHits { get; set; }
        public DateTime Timestamp { get; set; }
    }
}