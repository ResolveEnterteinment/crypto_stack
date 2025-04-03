using Application.Contracts.Requests.Asset;
using Domain.DTOs;
using Domain.DTOs.Asset;
using Domain.Models.Asset;

namespace Application.Interfaces
{
    public interface IAssetService : IRepository<AssetData>
    {
        public Task<ResultWrapper<AssetData>> GetFromSymbolAsync(string symbol);
        public Task<ResultWrapper<AssetData>> GetByTickerAsync(string symbol);
        public Task<ResultWrapper<IEnumerable<string>>> GetSupportedTickersAsync();
        public Task<ResultWrapper<IEnumerable<AssetDto>>> GetSupportedAssetsAsync();
        public Task<ResultWrapper<Guid>> CreateAsync(AssetCreateRequest request);
    }
}