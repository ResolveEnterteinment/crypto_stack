using Application.Interfaces;
using Application.Interfaces.Base;
using Application.Interfaces.Logging;
using Application.Interfaces.Network;
using Domain.Constants.Logging;
using Domain.DTOs;
using Domain.DTOs.Logging;
using Domain.DTOs.Network;
using Domain.Models.Network;
using Infrastructure.Services.Base;
using MongoDB.Driver;
using System.Text.RegularExpressions;

namespace Infrastructure.Services.Network
{
    public class NetworkService : BaseService<NetworkData>, INetworkService
    {
        private const string CACHE_KEY_NETWORKS_BY_ASSET = "networks:asset:{0}";
        private const string CACHE_KEY_NETWORK_BY_NAME = "network:name:{0}";
        private const string CACHE_KEY_ALL_NETWORKS = "networks:all";
        private const string CACHE_KEY_ACTIVE_NETWORKS = "networks:active";
        private const string CACHE_KEY_NETWORK_MEMO_CHECK = "network:memo:{0}";
        private const string CACHE_KEY_ADDRESS_VALIDATION = "address:validation:{0}:{1}";

        // Cache durations - networks are relatively static so longer durations are appropriate
        private static readonly TimeSpan NETWORK_CACHE_DURATION = TimeSpan.FromDays(1);
        private static readonly TimeSpan NETWORKS_COLLECTION_CACHE_DURATION = TimeSpan.FromHours(12);
        private static readonly TimeSpan VALIDATION_CACHE_DURATION = TimeSpan.FromMinutes(30);

        public NetworkService(
            IServiceProvider serviceProvider
        ) : base(
            serviceProvider,
            new()
            {
                IndexModels = [
                    new CreateIndexModel<NetworkData>(
                        Builders<NetworkData>.IndexKeys.Ascending(n => n.Name),
                        new CreateIndexOptions { Name = "Name_1", Unique = true }),
                    new CreateIndexModel<NetworkData>(
                        Builders<NetworkData>.IndexKeys.Ascending(n => n.SupportedAssets),
                        new CreateIndexOptions { Name = "SupportedAssets_1" }),
                    new CreateIndexModel<NetworkData>(
                        Builders<NetworkData>.IndexKeys.Ascending(n => n.IsActive),
                        new CreateIndexOptions { Name = "IsActive_1" })
                    ]
            })
        {
            _ = EnsureDefaultNetworksAsync();
        }

        private async Task EnsureDefaultNetworksAsync()
        {
            // Check if any networks exist, if not, seed default networks
            var networksExist = await _repository.CountAsync(FilterDefinition<NetworkData>.Empty) > 0;
            if (!networksExist)
            {
                _loggingService.LogInformation("Initializing default cryptocurrency networks");

                var defaultNetworks = new List<NetworkData>
                {
                    new() {
                        Id = Guid.NewGuid(),
                        Name = "Bitcoin",
                        TokenStandard = "BTC",
                        RequiresMemo = false,
                        AddressRegex = "^(bc1|[13])[a-zA-HJ-NP-Z0-9]{25,62}$",
                        AddressMinLength = 26,
                        AddressMaxLength = 90,
                        IsActive = true,
                        SupportedAssets = ["BTC"]
                    },
                    new() {
                        Id = Guid.NewGuid(),
                        Name = "Ethereum",
                        TokenStandard = "ERC20",
                        RequiresMemo = false,
                        AddressRegex = "^0x[a-fA-F0-9]{40}$",
                        AddressMinLength = 42,
                        AddressMaxLength = 42,
                        IsActive = true,
                        SupportedAssets = ["ETH", "USDT", "USDC", "LINK"]
                    },
                    new() {
                        Id = Guid.NewGuid(),
                        Name = "Tron",
                        TokenStandard = "TRC20",
                        RequiresMemo = false,
                        AddressRegex = "^T[a-zA-Z0-9]{33}$",
                        AddressMinLength = 34,
                        AddressMaxLength = 34,
                        IsActive = true,
                        SupportedAssets = ["TRX", "USDT"]
                    },
                    new() {
                        Id = Guid.NewGuid(),
                        Name = "Binance Smart Chain",
                        TokenStandard = "BEP20",
                        RequiresMemo = false,
                        AddressRegex = "^0x[a-fA-F0-9]{40}$",
                        AddressMinLength = 42,
                        AddressMaxLength = 42,
                        IsActive = true,
                        SupportedAssets = ["BNB", "USDT"]
                    },
                    new() {
                        Id = Guid.NewGuid(),
                        Name = "Ripple",
                        TokenStandard = "XRP",
                        RequiresMemo = true,
                        AddressRegex = "^r[0-9a-zA-Z]{24,34}$",
                        AddressMinLength = 25,
                        AddressMaxLength = 35,
                        IsActive = true,
                        SupportedAssets = ["XRP"]
                    },
                    new() {
                        Id = Guid.NewGuid(),
                        Name = "Solana",
                        TokenStandard = "SPL",
                        RequiresMemo = false,
                        AddressRegex = "^[1-9A-HJ-NP-Za-km-z]{32,44}$",
                        AddressMinLength = 32,
                        AddressMaxLength = 44,
                        IsActive = true,
                        SupportedAssets = ["SOL", "USDC"]
                    },
                    new() {
                        Id = Guid.NewGuid(),
                        Name = "Cardano",
                        TokenStandard = "ADA",
                        RequiresMemo = false,
                        AddressRegex = "^(addr1|stake1)[0-9a-z]{53,98}$",
                        AddressMinLength = 59,
                        AddressMaxLength = 104,
                        IsActive = true,
                        SupportedAssets = ["ADA"]
                    }
                };

                foreach (var network in defaultNetworks)
                {
                    var insertResult = await InsertAsync(network);
                    if (insertResult != null && insertResult.IsSuccess)
                    {
                        _loggingService.LogInformation("Successfully inserted network: {NetworkName}", network.Name);
                    }
                }

                _loggingService.LogInformation($"Successfully initialized {defaultNetworks.Count} default networks");

                // Invalidate caches after seeding
                await InvalidateAllRelatedCachesAsync();
            }
        }

        public async Task<ResultWrapper<List<NetworkDto>>> GetNetworksByAssetAsync(string assetTicker)
        {
            if (string.IsNullOrEmpty(assetTicker))
            {
                return ResultWrapper<List<NetworkDto>>.Failure(
                    Domain.Constants.FailureReason.ValidationError,
                    "Asset ticker is required");
            }

            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Network",
                    FileName = "NetworkService",
                    OperationName = "GetNetworksByAssetAsync(string assetTicker)",
                    State =
                    {
                        ["Ticker"] = assetTicker,
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    var normalizedTicker = assetTicker.Trim().ToUpperInvariant();
                    var cacheKey = string.Format(CACHE_KEY_NETWORKS_BY_ASSET, normalizedTicker);

                    var cachedNetworks = await _cacheService.GetAnyCachedAsync(
                        cacheKey,
                        async () =>
                        {
                            // Query for networks that support this asset and are active
                            var filter = Builders<NetworkData>.Filter.And(
                                Builders<NetworkData>.Filter.AnyEq(n => n.SupportedAssets, normalizedTicker),
                                Builders<NetworkData>.Filter.Eq(n => n.IsActive, true)
                            );

                            var networkResults = await GetManyAsync(filter);
                            if (networkResults is null || !networkResults.IsSuccess)
                            {
                                throw new Exception("Network results returned null");
                            }

                            // Convert to DTOs
                            return networkResults.Data.Select(n => new NetworkDto
                            {
                                Name = n.Name,
                                TokenStandard = n.TokenStandard,
                                RequiresMemo = n.RequiresMemo
                            }).ToList();
                        },
                        NETWORK_CACHE_DURATION
                    );

                    return cachedNetworks ?? [];
                })
                .WithMongoDbReadResilience()
                .WithPerformanceMonitoring(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3))
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<NetworkDto?>> GetNetworkByNameAsync(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return ResultWrapper<NetworkDto?>.Failure(
                    Domain.Constants.FailureReason.ValidationError,
                    "Network name is required");
            }

            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Network",
                    FileName = "NetworkService",
                    OperationName = "GetNetworkByNameAsync(string name)",
                    State =
                    {
                        ["Name"] = name,
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    var normalizedName = name.Trim();
                    var cacheKey = string.Format(CACHE_KEY_NETWORK_BY_NAME, normalizedName.ToLowerInvariant());

                    var cachedNetwork = await _cacheService.GetAnyCachedAsync(
                        cacheKey,
                        async () =>
                        {
                            var filter = Builders<NetworkData>.Filter.Eq(n => n.Name, normalizedName);
                            var networkResult = await GetOneAsync(filter);

                            if (!networkResult.IsSuccess || networkResult.Data == null)
                            {
                                throw new KeyNotFoundException($"Network '{normalizedName}' not found");
                            }

                            var network = networkResult.Data;
                            return new NetworkDto
                            {
                                Name = network.Name,
                                TokenStandard = network.TokenStandard,
                                RequiresMemo = network.RequiresMemo
                            };
                        },
                        NETWORK_CACHE_DURATION
                    );

                    return cachedNetwork;
                })
                .WithMongoDbReadResilience()
                .WithPerformanceMonitoring(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2))
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<bool>> IsCryptoAddressValidAsync(string network, string address)
        {
            if (string.IsNullOrEmpty(network) || string.IsNullOrEmpty(address))
            {
                return ResultWrapper<bool>.Failure(
                    Domain.Constants.FailureReason.ValidationError,
                    "Network name and address are required");
            }

            return await _resilienceService.CreateBuilder(
               new Scope
               {
                   NameSpace = "Infrastructure.Services.Network",
                   FileName = "NetworkService",
                   OperationName = "IsCryptoAddressValidAsync(string network, string address)",
                   State =
                        {
                            ["Network"] = network,
                            ["Address"] = address,
                        },
                   LogLevel = LogLevel.Error
               },
                async () =>
                {
                    var normalizedNetwork = network.Trim();
                    var normalizedAddress = address.Trim();

                    // Create cache key for address validation
                    var addressHash = normalizedAddress.Length > 20
                        ? $"{normalizedAddress[..10]}...{normalizedAddress[^10..]}"
                        : normalizedAddress;
                    var cacheKey = string.Format(CACHE_KEY_ADDRESS_VALIDATION, normalizedNetwork, addressHash);

                    var cachedResult = await _cacheService.GetAnyCachedAsync(
                        cacheKey,
                        async () =>
                        {
                            var networkResult = await GetOneAsync(
                                Builders<NetworkData>.Filter.Eq(n => n.Name, normalizedNetwork));

                            if (networkResult == null || !networkResult.IsSuccess || networkResult.Data == null)
                            {
                                throw new KeyNotFoundException($"Network '{normalizedNetwork}' not found");
                            }

                            var networkData = networkResult.Data;

                            // Check basic length validation
                            if (normalizedAddress.Length < networkData.AddressMinLength ||
                                normalizedAddress.Length > networkData.AddressMaxLength)
                            {
                                return false;
                            }

                            // Check regex pattern if available
                            if (!string.IsNullOrEmpty(networkData.AddressRegex))
                            {
                                var isValid = Regex.IsMatch(normalizedAddress, networkData.AddressRegex);
                                return isValid;
                            }

                            // If no specific validation, consider it invalid
                            return false;
                        },
                        VALIDATION_CACHE_DURATION
                    );

                    return cachedResult;
                })
                .WithQuickOperationResilience()
                .WithPerformanceMonitoring(TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(2))
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<bool>> RequiresMemoAsync(string network)
        {
            if (string.IsNullOrEmpty(network))
            {
                return ResultWrapper<bool>.Failure(
                    Domain.Constants.FailureReason.ValidationError,
                    "Network name is required");
            }

            return await _resilienceService.CreateBuilder(
               new Scope
               {
                   NameSpace = "Infrastructure.Services.Network",
                   FileName = "NetworkService",
                   OperationName = "RequiresMemoAsync(string network)",
                   State =
                        {
                            ["Network"] = network,
                        },
                   LogLevel = LogLevel.Error
               },
                async () =>
                {
                    var normalizedNetwork = network.Trim();
                    var cacheKey = string.Format(CACHE_KEY_NETWORK_MEMO_CHECK, normalizedNetwork.ToLowerInvariant());

                    var cachedResult = await _cacheService.GetAnyCachedAsync(
                        cacheKey,
                        async () =>
                        {
                            var filter = Builders<NetworkData>.Filter.Eq(n => n.Name, normalizedNetwork);
                            var networkResult = await GetOneAsync(filter);
                            if (networkResult == null || !networkResult.IsSuccess)
                            {
                                throw new KeyNotFoundException($"Network '{normalizedNetwork}' not found");
                            }

                            return networkResult.Data.RequiresMemo;
                        },
                        NETWORK_CACHE_DURATION
                    );

                    return cachedResult;
                })
                .WithQuickOperationResilience()
                .WithPerformanceMonitoring(TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(1))
                .ExecuteAsync();
        }

        /// <summary>
        /// Gets all active networks with caching
        /// </summary>
        public async Task<ResultWrapper<List<NetworkDto>>> GetAllActiveNetworksAsync()
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Network",
                    FileName = "NetworkService",
                    OperationName = "GetAllActiveNetworksAsync()",
                    State = [],
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    var cachedNetworks = await _cacheService.GetAnyCachedAsync(
                        CACHE_KEY_ACTIVE_NETWORKS,
                        async () =>
                        {
                            var filter = Builders<NetworkData>.Filter.Eq(n => n.IsActive, true);
                            var networkResults = await GetManyAsync(filter);

                            if (networkResults is null || !networkResults.IsSuccess)
                            {
                                throw new Exception("Failed to fetch active networks");
                            }

                            return networkResults.Data.Select(n => new NetworkDto
                            {
                                Name = n.Name,
                                TokenStandard = n.TokenStandard,
                                RequiresMemo = n.RequiresMemo
                            }).ToList();
                        },
                        NETWORKS_COLLECTION_CACHE_DURATION
                    );

                    return cachedNetworks ?? [];
                })
                .WithMongoDbReadResilience()
                .WithPerformanceMonitoring(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3))
                .ExecuteAsync();
        }

        /// <summary>
        /// Gets all networks (active and inactive) with caching
        /// </summary>
        public async Task<ResultWrapper<List<NetworkDto>>> GetAllNetworksAsync()
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Network",
                    FileName = "NetworkService",
                    OperationName = "GetAllNetworksAsync()",
                    State = [],
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    var cachedNetworks = await _cacheService.GetAnyCachedAsync(
                        CACHE_KEY_ALL_NETWORKS,
                        async () =>
                        {
                            var networkResults = await GetAllAsync();

                            if (networkResults is null || !networkResults.IsSuccess)
                            {
                                throw new Exception("Failed to fetch all networks");
                            }

                            return networkResults.Data.Select(n => new NetworkDto
                            {
                                Name = n.Name,
                                TokenStandard = n.TokenStandard,
                                RequiresMemo = n.RequiresMemo
                            }).ToList();
                        },
                        NETWORKS_COLLECTION_CACHE_DURATION
                    );

                    return cachedNetworks ?? [];
                })
                .WithMongoDbReadResilience()
                .WithPerformanceMonitoring(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3))
                .ExecuteAsync();
        }

        /// <summary>
        /// Invalidates all network-related caches
        /// </summary>
        public async Task InvalidateAllRelatedCachesAsync()
        {
            try
            {
                var cacheKeysToInvalidate = new List<string>
                {
                    CACHE_KEY_ALL_NETWORKS,
                    CACHE_KEY_ACTIVE_NETWORKS,
                    _cacheService.GetCollectionCacheKey()
                };

                foreach (var cacheKey in cacheKeysToInvalidate)
                {
                    _cacheService.Invalidate(cacheKey);
                }

                // Invalidate pattern-based cache keys (this would require a more advanced cache service)
                // For now, we'll log that these need manual invalidation
                _loggingService.LogInformation("Invalidated {Count} network cache keys. Pattern-based keys (asset-specific, name-specific) may need manual invalidation",
                    cacheKeysToInvalidate.Count);
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Failed to invalidate network caches: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Invalidates caches for a specific network
        /// </summary>
        /// <param name="networkName">The network name to invalidate caches for</param>
        public async Task InvalidateNetworkCacheAsync(string networkName)
        {
            if (string.IsNullOrWhiteSpace(networkName))
                return;

            try
            {
                var normalizedName = networkName.Trim().ToLowerInvariant();
                var cacheKeysToInvalidate = new List<string>
                {
                    string.Format(CACHE_KEY_NETWORK_BY_NAME, normalizedName),
                    string.Format(CACHE_KEY_NETWORK_MEMO_CHECK, normalizedName),
                    CACHE_KEY_ALL_NETWORKS,
                    CACHE_KEY_ACTIVE_NETWORKS
                };

                foreach (var cacheKey in cacheKeysToInvalidate)
                {
                    _cacheService.Invalidate(cacheKey);
                }

                _loggingService.LogInformation("Invalidated caches for network: {NetworkName}", networkName);
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Failed to invalidate caches for network {NetworkName}: {Error}", networkName, ex.Message);
            }
        }

        /// <summary>
        /// Warms up the cache for essential networks
        /// </summary>
        public async Task<ResultWrapper> WarmupEssentialNetworkCacheAsync()
        {
            try
            {
                _loggingService.LogInformation("Warming up essential network caches");

                // Pre-load essential networks
                var essentialNetworks = new[] { "Bitcoin", "Ethereum", "Tron", "Binance Smart Chain" };
                var warmupTasks = essentialNetworks.Select(GetNetworkByNameAsync).ToArray();

                // Pre-load all active networks
                var allActiveNetworksTask = GetAllActiveNetworksAsync();

                // Wait for all warmup tasks
                await Task.WhenAll(warmupTasks.Concat(new Task[] { allActiveNetworksTask }));

                _loggingService.LogInformation("Successfully warmed up essential network caches");
                return ResultWrapper.Success("Essential network caches warmed up successfully");
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Error warming up essential network caches: {Error}", ex.Message);
                return ResultWrapper.FromException(ex);
            }
        }

        /// <summary>
        /// Gets cache statistics for monitoring
        /// </summary>
        public async Task<ResultWrapper<NetworkCacheStats>> GetCacheStatsAsync()
        {
            try
            {
                var stats = new NetworkCacheStats
                {
                    AllNetworksExists = _cacheService.TryGetValue<List<NetworkDto>>(CACHE_KEY_ALL_NETWORKS, out _),
                    ActiveNetworksExists = _cacheService.TryGetValue<List<NetworkDto>>(CACHE_KEY_ACTIVE_NETWORKS, out _),
                    Timestamp = DateTime.UtcNow
                };

                // Check some common network caches
                var commonNetworks = new[] { "Bitcoin", "Ethereum", "Tron" };
                stats.CommonNetworkCacheHits = 0;
                foreach (var network in commonNetworks)
                {
                    var cacheKey = string.Format(CACHE_KEY_NETWORK_BY_NAME, network.ToLowerInvariant());
                    if (_cacheService.TryGetValue<NetworkDto>(cacheKey, out _))
                    {
                        stats.CommonNetworkCacheHits++;
                    }
                }

                return ResultWrapper<NetworkCacheStats>.Success(stats);
            }
            catch (Exception ex)
            {
                return ResultWrapper<NetworkCacheStats>.FromException(ex);
            }
        }
    }

    /// <summary>
    /// Cache statistics for monitoring network cache health
    /// </summary>
    public class NetworkCacheStats
    {
        public bool AllNetworksExists { get; set; }
        public bool ActiveNetworksExists { get; set; }
        public int CommonNetworkCacheHits { get; set; }
        public DateTime Timestamp { get; set; }
    }
}