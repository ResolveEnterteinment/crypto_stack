using Domain.Models.Crypto;
using MongoDB.Bson;

namespace Infrastructure.Services
{
    public interface ICoinService
    {
        public Task<CoinData?> GetCoinDataAsync(ObjectId coinId);
        public Task<CoinData?> GetCryptoFromSymbolAsync(string symbol);
    }
}