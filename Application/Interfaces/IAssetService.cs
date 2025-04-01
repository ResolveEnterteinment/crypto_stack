using Domain.DTOs;
using Domain.Interfaces;
using Domain.Models.Asset;

namespace Application.Interfaces
{
    public interface IAssetService : IRepository<AssetData>
    {
        public Task<ResultWrapper<AssetData>> GetFromSymbolAsync(string symbol);
        public Task<ResultWrapper<AssetData>> GetByTickerAsync(string symbol);
        public Task<ResultWrapper<IEnumerable<string>>> GetSupportedTickersAsync();
    }
}