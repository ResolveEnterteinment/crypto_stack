using Application.Interfaces.Base;
using Domain.Models.Asset;

namespace Application.Interfaces.Asset
{
    public interface IAssetRepository : ICrudRepository<AssetData>
    {
    }
}

