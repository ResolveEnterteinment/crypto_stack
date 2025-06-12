using Application.Contracts.Requests.Asset;
using Application.Interfaces.Base;
using Domain.DTOs;
using Domain.DTOs.Asset;
using Domain.Models.Asset;

namespace Application.Interfaces.Asset
{
    public interface IAssetService : IBaseService<AssetData>
    {
        Task<ResultWrapper<AssetData>> GetByTickerAsync(string ticker);
        Task<ResultWrapper<AssetData>> GetFromSymbolAsync(string symbol);
        Task<ResultWrapper<IEnumerable<string>>> GetSupportedTickersAsync();
        Task<ResultWrapper<IEnumerable<AssetDto>>> GetSupportedAssetsAsync();
        Task<ResultWrapper<List<AssetData>>> GetManyByTickersAsync(IEnumerable<string> tickers);
        Task<ResultWrapper<Guid>> CreateAsync(AssetCreateRequest request);
    }
}