using Domain.Models.Crypto;
using MongoDB.Bson;

namespace Infrastructure.Services
{
    public interface ICoinService
    {
        public CoinData? GetCoinData(ObjectId coinId);
        public CoinData? GetCryptoFromSymbol(string symbol);
    }
}