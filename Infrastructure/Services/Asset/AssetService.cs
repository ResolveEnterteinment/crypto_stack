using Application.Contracts.Requests.Asset;
using Application.Interfaces;
using Application.Interfaces.Asset;
using Application.Interfaces.Base;
using Application.Interfaces.Logging;
using Domain.Constants;
using Domain.Constants.Asset;
using Domain.DTOs;
using Domain.DTOs.Asset;
using Domain.Exceptions;
using Infrastructure.Services.Base;
using MongoDB.Driver;
using AssetData = Domain.Models.Asset.AssetData;

namespace Infrastructure.Services.Asset
{
    public class AssetService : BaseService<AssetData>, IAssetService
    {
        private const string CACHE_KEY_ASSET_TICKER = "asset_ticker:{0}";
        private const string CACHE_KEY_ASSET_SYMBOL = "asset_symbol:{0}";
        private const string CACHE_KEY_SUPPORTED_TICKERS = "supported_tickers";
        private const string CACHE_KEY_SUPPORTED_ASSETS = "supported_assets";

        private readonly ICacheService<AssetData> _cache;

        public AssetService(
            ICrudRepository<AssetData> repo,
            ICacheService<AssetData> cache,
            IMongoIndexService<AssetData> indexService,
            ILoggingService logger,
            IEventService eventService)
            : base(
                repo,
                cache,
                indexService,
                logger,
                eventService,
                new[]
                {
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
                })
        {
            _cache = cache;
            _ = InitializeEssentialAssetsAsync();
        }

        private async Task InitializeEssentialAssetsAsync()
        {
            if (await GetByTickerAsync("BTC") == null)
            {
                _ = await InsertAsync(new AssetData { Name = "Bitcoin", Ticker = "BTC", Precision = 18, Symbol = "₿", Exchange = "Binance", Type = "EXCHANGE", Class = "CRYPTO" });
            }

            if (await GetByTickerAsync("USDT") == null)
            {
                _ = await InsertAsync(new AssetData { Name = "Tether USD", Ticker = "USDT", Precision = 6, Symbol = "₮", Exchange = "Binance", Type = AssetType.Exchange, Class = "STABLECOIN" });
            }
        }

        public Task<ResultWrapper<Guid>> CreateAsync(AssetCreateRequest req)
        {
            return SafeExecute(async () =>
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

                        if(insertResult is null || !insertResult.IsSuccess)
                        {
                            throw new DatabaseException(insertResult?.ErrorMessage ?? "Insert result returned null");
                        }

                        var insertedAsset = insertResult.Data;

                        return insertedAsset.AffectedIds.ToList()[0];
                    });
        }

        public Task<ResultWrapper> UpdateAsync(Guid id, AssetUpdateRequest req)
        {
            return SafeExecute(async () =>
                    {
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
                    });
        }

        /// <summary>
        /// Gets multiple assets by their ticker symbols
        /// </summary>
        /// <param name="tickers">Collection of ticker symbols to search for</param>
        /// <returns>List of matching assets</returns>
        public async Task<ResultWrapper<List<AssetData>>> GetManyByTickersAsync(IEnumerable<string> tickers)
        {
            try
            {
                if (tickers == null || !tickers.Any())
                {
                    return ResultWrapper<List<AssetData>>.Success(new List<AssetData>());
                }

                // Convert to array and normalize tickers (uppercase, trim, remove duplicates)
                var normalizedTickers = tickers
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(t => t.Trim().ToUpperInvariant())
                    .Distinct()
                    .ToArray();

                if (normalizedTickers.Length == 0)
                {
                    return ResultWrapper<List<AssetData>>.Success(new List<AssetData>());
                }

                // 🔧 FIX: Use Filter.In instead of Filter.AnyIn
                // AnyIn is for array fields, In is for simple field matching
                var filter = Builders<AssetData>.Filter.In(a => a.Ticker, normalizedTickers);

                // Alternative syntax if strongly-typed doesn't work:
                // var filter = Builders<AssetData>.Filter.In("Ticker", normalizedTickers);

                return await GetManyAsync(filter);
            }
            catch (Exception ex)
            {
                Logger.LogError("Error fetching assets by tickers: {ErrorMessage}", ex.Message);
                return ResultWrapper<List<AssetData>>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<AssetData>> GetByTickerAsync(string ticker)
        {
            return await GetOneAsync(Builders<AssetData>.Filter.Where(a => a.Ticker.Equals(ticker, StringComparison.OrdinalIgnoreCase)));
        }

        public async Task<ResultWrapper<AssetData>> GetFromSymbolAsync(string symbol)
        {
            return await GetOneAsync(Builders<AssetData>.Filter.Where(a => symbol.StartsWith(a.Symbol, StringComparison.OrdinalIgnoreCase)));
        }

        public Task<ResultWrapper<IEnumerable<AssetDto>>> GetSupportedAssetsAsync()
        {
            return SafeExecute(async () =>
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
            });
        }

        public Task<ResultWrapper<IEnumerable<string>>> GetSupportedTickersAsync()
        {
            return SafeExecute(async () =>
            {
                var result = await _repository.GetAllAsync().ContinueWith(t => t.Result?.Select(a => a.Ticker));

                if (result == null)
                    throw new AssetFetchException("Failed to fetch supported tickers.");

                return result;
            });
        }
    }
}