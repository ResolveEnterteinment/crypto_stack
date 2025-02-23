using Domain.Interfaces;
using Domain.Models.Crypto;

namespace Infrastructure.Services
{
    public interface ICoinService : IRepository<CoinData>
    {
        public Task<CoinData?> GetCryptoFromSymbolAsync(string symbol);
    }
}