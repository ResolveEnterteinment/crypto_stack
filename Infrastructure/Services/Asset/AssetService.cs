using Application.Contracts.Requests.Asset;
using Application.Interfaces;
using Application.Interfaces.Asset;
using Application.Interfaces.Base;
using Application.Interfaces.Logging;
using Domain.Constants;
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
                    new CreateIndexOptions { Name = "Class_1" })
                })
        {
            _cache = cache;
            _ = InitializeEssentialAssetsAsync();
        }

        private async Task InitializeEssentialAssetsAsync()
        {
            if (await GetByTickerAsync("BTC") == null)
                await InsertAsync(new AssetData { Name = "Bitcoin", Ticker = "BTC", Precision = 18, Symbol = "₿", Exchange = "Binance" });

            if (await GetByTickerAsync("USDT") == null)
                await InsertAsync(new AssetData { Name = "Tether USD", Ticker = "USDT", Precision = 6, Symbol = "₮", Exchange = "Binance", Type = AssetType.Exchange });
        }

        public Task<ResultWrapper<Guid>> CreateAsync(AssetCreateRequest req)
            => SafeExecute(async () =>
            {
                var asset = new AssetData
                {
                    Name = req.Name,
                    Ticker = req.Ticker.ToUpper(),
                    Symbol = req.Symbol,
                    Precision = req.Precision,
                    SubunitName = req.SubunitName,
                    Exchange = req.Exchange,
                    Type = req.Type
                };

                await InsertAsync(asset);
                // Clear any list‑caches
                _cache.Invalidate(CACHE_KEY_SUPPORTED_TICKERS);
                _cache.Invalidate(CACHE_KEY_SUPPORTED_ASSETS);
                return asset.Id;
            });

        public Task<ResultWrapper> UpdateAsync(Guid id, AssetUpdateRequest req)
            => SafeExecute(async () =>
            {
                var fields = new Dictionary<string, object>();
                if (!string.IsNullOrWhiteSpace(req.Name)) fields["Name"] = req.Name;
                if (!string.IsNullOrWhiteSpace(req.Ticker)) fields["Ticker"] = req.Ticker;
                if (!string.IsNullOrWhiteSpace(req.Symbol)) fields["Symbol"] = req.Symbol;
                if (req.Precision.HasValue) fields["Precision"] = req.Precision.Value;
                if (!string.IsNullOrWhiteSpace(req.SubunitName)) fields["SubunitName"] = req.SubunitName;
                if (!string.IsNullOrWhiteSpace(req.Exchange)) fields["Exchange"] = req.Exchange;
                if (!string.IsNullOrWhiteSpace(req.Type)) fields["Type"] = req.Type;

                await UpdateAsync(id, fields);
                _cache.Invalidate(string.Format(CACHE_KEY_ASSET_TICKER, req.Ticker.ToLowerInvariant()));
                _cache.Invalidate(string.Format(CACHE_KEY_ASSET_SYMBOL, req.Symbol.ToLowerInvariant()));
                _cache.Invalidate(CACHE_KEY_SUPPORTED_TICKERS);
                _cache.Invalidate(CACHE_KEY_SUPPORTED_ASSETS);
            });

        public Task<ResultWrapper<AssetData>> GetByTickerAsync(string ticker) =>
            FetchCached(
                string.Format(CACHE_KEY_ASSET_TICKER, ticker.ToLowerInvariant()),
                () => Repository.GetOneAsync(Builders<AssetData>.Filter.Where(a => a.Ticker.Equals(ticker, StringComparison.OrdinalIgnoreCase))),
                TimeSpan.FromMinutes(30),
                () => throw new ResourceNotFoundException("Asset", ticker)
            );

        public Task<ResultWrapper<AssetData>> GetFromSymbolAsync(string symbol) =>
            FetchCached(
                string.Format(CACHE_KEY_ASSET_SYMBOL, symbol.ToLowerInvariant()),
                () => Repository.GetOneAsync(Builders<AssetData>.Filter.Where(a => symbol.StartsWith(a.Symbol, StringComparison.OrdinalIgnoreCase))),
                TimeSpan.FromMinutes(30),
                () => throw new KeyNotFoundException($"No asset found for symbol {symbol}")
            );

        public Task<ResultWrapper<IEnumerable<AssetDto>>> GetSupportedAssetsAsync() =>
            FetchCached(
                CACHE_KEY_SUPPORTED_ASSETS,
                 () => Repository.GetAllAsync(Builders<AssetData>.Filter.Eq(a => a.Type, AssetType.Exchange))
                 .ContinueWith(t => t.Result?.Select(a => new AssetDto(a))),
                TimeSpan.FromMinutes(30)
            );

        public Task<ResultWrapper<IEnumerable<string>>> GetSupportedTickersAsync() =>
            FetchCached(
                CACHE_KEY_SUPPORTED_TICKERS,
                () => Repository.GetAllAsync().ContinueWith(t => t.Result?.Select(a => a.Ticker)),
                TimeSpan.FromMinutes(30)
            );
    }
}