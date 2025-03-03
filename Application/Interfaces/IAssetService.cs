using Domain.DTOs;
using Domain.Interfaces;
using Domain.Models.Crypto;

namespace Infrastructure.Services
{
    public interface IAssetService : IRepository<AssetData>
    {
        public Task<ResultWrapper<AssetData>> GetFromSymbolAsync(string symbol);
        public Task<ResultWrapper<AssetData>> GetByTickerAsync(string symbol);
        public Task<ResultWrapper<IEnumerable<string>>> GetSupportedTickersAsync();
    }
}