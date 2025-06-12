using Application.Interfaces;
using Application.Interfaces.Base;
using Application.Interfaces.Logging;
using Application.Interfaces.Network;
using Domain.DTOs;
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
        private readonly TimeSpan CACHE_DURATION = TimeSpan.FromHours(1);

        public NetworkService(
            ICrudRepository<NetworkData> repository,
            ICacheService<NetworkData> cacheService,
            IMongoIndexService<NetworkData> indexService,
            ILoggingService logger,
            IEventService eventService
        ) : base(
            repository,
            cacheService,
            indexService,
            logger,
            eventService,
            new[] {
                new CreateIndexModel<NetworkData>(
                    Builders<NetworkData>.IndexKeys.Ascending(n => n.Name),
                    new CreateIndexOptions { Name = "Name_1", Unique = true }),
                new CreateIndexModel<NetworkData>(
                    Builders<NetworkData>.IndexKeys.Ascending(n => n.SupportedAssets),
                    new CreateIndexOptions { Name = "SupportedAssets_1" })
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
                Logger.LogInformation("Initializing default cryptocurrency networks");

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
                        SupportedAssets = ["ETH", "USDT", "USDC"]
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
                    _ = await _repository.InsertAsync(network);
                }

                Logger.LogInformation($"Successfully initialized {defaultNetworks.Count} default networks");
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

            try
            {
                // Use cache to avoid frequent database lookups
                return await FetchCached(
                    string.Format(CACHE_KEY_NETWORKS_BY_ASSET, assetTicker.ToUpperInvariant()),
                    async () =>
                    {
                        // Query for networks that support this asset and are active
                        var filter = Builders<NetworkData>.Filter.And(
                            Builders<NetworkData>.Filter.AnyEq(n => n.SupportedAssets, assetTicker.ToUpperInvariant()),
                            Builders<NetworkData>.Filter.Eq(n => n.IsActive, true)
                        );

                        var networkResults = await _repository.GetAllAsync(filter);
                        if (networkResults is null)
                        {
                            throw new Exception("Network results returned null");
                        }

                        // Convert to DTOs
                        return networkResults.Select(n => new NetworkDto
                        {
                            Name = n.Name,
                            TokenStandard = n.TokenStandard,
                            RequiresMemo = n.RequiresMemo
                        }).ToList();
                    },
                    CACHE_DURATION);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error getting networks for asset {assetTicker}: {ex.Message}");
                return ResultWrapper<List<NetworkDto>>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<NetworkDto>> GetNetworkByNameAsync(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return ResultWrapper<NetworkDto>.Failure(
                    Domain.Constants.FailureReason.ValidationError,
                    "Network name is required");
            }

            try
            {
                return await FetchCached<NetworkDto>(
                    string.Format(CACHE_KEY_NETWORK_BY_NAME, name.ToLowerInvariant()),
                    async () =>
                    {
                        var filter = Builders<NetworkData>.Filter.Eq(n => n.Name, name);
                        var networkResult = await GetOneAsync(filter);

                        if (!networkResult.IsSuccess || networkResult.Data == null)
                        {
                            throw new KeyNotFoundException($"Network '{name}' not found");
                        }

                        var network = networkResult.Data;
                        return new NetworkDto
                        {
                            Name = network.Name,
                            TokenStandard = network.TokenStandard,
                            RequiresMemo = network.RequiresMemo
                        };
                    },
                    CACHE_DURATION);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error getting network '{name}': {ex.Message}");
                return ResultWrapper<NetworkDto>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<bool>> IsAddressValidAsync(string network, string address)
        {
            if (string.IsNullOrEmpty(network) || string.IsNullOrEmpty(address))
            {
                return ResultWrapper<bool>.Failure(
                    Domain.Constants.FailureReason.ValidationError,
                    "Network name and address are required");
            }

            try
            {
                var networkResult = await GetOneAsync(
                    Builders<NetworkData>.Filter.Eq(n => n.Name, network));

                if (!networkResult.IsSuccess || networkResult.Data == null)
                {
                    return ResultWrapper<bool>.Failure(
                        Domain.Constants.FailureReason.NotFound,
                        $"Network '{network}' not found");
                }

                var networkData = networkResult.Data;

                // Check basic length validation
                if (address.Length < networkData.AddressMinLength ||
                    address.Length > networkData.AddressMaxLength)
                {
                    return ResultWrapper<bool>.Success(false);
                }

                // Check regex pattern if available
                if (!string.IsNullOrEmpty(networkData.AddressRegex))
                {
                    var isValid = Regex.IsMatch(address, networkData.AddressRegex);
                    return ResultWrapper<bool>.Success(isValid);
                }

                // If no specific validation, consider it valid based on length
                return ResultWrapper<bool>.Success(true);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error validating address for network '{network}': {ex.Message}");
                return ResultWrapper<bool>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<bool>> RequiresMemoAsync(string network)
        {
            if (string.IsNullOrEmpty(network))
            {
                return ResultWrapper<bool>.Failure(
                    Domain.Constants.FailureReason.ValidationError,
                    "Network name is required");
            }

            try
            {
                var networkResult = await GetOneAsync(
                    Builders<NetworkData>.Filter.Eq(n => n.Name, network));

                return !networkResult.IsSuccess || networkResult.Data == null
                    ? ResultWrapper<bool>.Failure(
                        Domain.Constants.FailureReason.NotFound,
                        $"Network '{network}' not found")
                    : ResultWrapper<bool>.Success(networkResult.Data.RequiresMemo);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error checking memo requirement for network '{network}': {ex.Message}");
                return ResultWrapper<bool>.FromException(ex);
            }
        }
    }
}