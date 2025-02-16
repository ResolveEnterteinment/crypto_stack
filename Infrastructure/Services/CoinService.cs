using AspNetCore.Identity.MongoDbCore.Infrastructure;
using Domain.Models.Crypto;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Infrastructure.Services
{
    public class CoinService : ICoinService
    {
        private readonly IMongoCollection<CoinData> _coinCollection;
        private readonly ILogger<CoinService> _logger;
        public CoinService(IOptions<MongoDbSettings> mongoDbSettings, ILogger<CoinService> logger)
        {
            // Assume your MongoDB database has collections named "transactions" and "subscriptions"
            var mongoClient = new MongoClient(mongoDbSettings.Value.ConnectionString);

            var mongoDatabase = mongoClient.GetDatabase(
                mongoDbSettings.Value.DatabaseName);
            _coinCollection = mongoDatabase.GetCollection<CoinData>("coins");
            _logger = logger;
        }
        /// <summary>
        /// Gets crypto data for a given symbol.
        /// </summary>
        /// <param name="symbol">The trading pair symbol (e.g., BTCUSDT).</param>
        /// <returns>A CryptoData object with information about the cryptocurrency.</returns>
        public CoinData? GetCryptoFromSymbol(string symbol)
        {
            var filter = Builders<CoinData>.Filter.Where(coin => symbol.Contains(coin.Ticker));
            var coin = _coinCollection.Find(filter).FirstOrDefault();
            if (coin == null)
            {
                _logger.LogError(new KeyNotFoundException(), "Error while fetching coin data from {symbol}", symbol);
                return null;
            }
            return coin;
        }

        public CoinData? GetCoinData(ObjectId coinId)
        {
            var filter = Builders<CoinData>.Filter
                .Eq(s => s._id, coinId);
            // Asynchronously retrieves the first document that matches the filter
            CoinData coin = _coinCollection.Find(filter).FirstOrDefault();
            if (coin == null)
            {
                _logger.LogError(new KeyNotFoundException(), "Error while fetching coin data with id #{0}", coinId.ToString());
                return null;
            }
            return coin;
        }
    }
}
