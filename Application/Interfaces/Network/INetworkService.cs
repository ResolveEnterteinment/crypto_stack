using Application.Interfaces.Base;
using Domain.DTOs;
using Domain.DTOs.Network;
using Domain.Models.Network;

namespace Application.Interfaces.Network
{
    public interface INetworkService : IBaseService<NetworkData>
    {
        Task<ResultWrapper<List<NetworkDto>>> GetNetworksByAssetAsync(string assetTicker);
        Task<ResultWrapper<NetworkDto?>> GetNetworkByNameAsync(string name);
        Task<ResultWrapper<bool>> IsCryptoAddressValidAsync(string network, string address);
        Task<ResultWrapper<bool>> RequiresMemoAsync(string network);
    }
}