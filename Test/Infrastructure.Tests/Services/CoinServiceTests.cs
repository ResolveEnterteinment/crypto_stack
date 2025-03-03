using AspNetCore.Identity.MongoDbCore.Infrastructure;
using Domain.Models.Crypto;
using Infrastructure.Services;
using Infrastructure.Tests.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;

namespace Infrastructure.Tests.Services
{
    public class CoinServiceTests
    {
        private readonly Mock<IMongoCollection<AssetData>> _mockCollection;
        private readonly Mock<IMongoDatabase> _mockDatabase;
        private readonly Mock<IMongoClient> _mockMongoClient;
        private readonly IOptions<MongoDbSettings> _mongoDbSettings;
        private readonly HttpClient _httpClient;
        private readonly ILogger<AssetService> _logger;
        private readonly IConfiguration _configuration;

        public CoinServiceTests()
        {
            _mongoDbSettings = Options.Create(new MongoDbSettings
            {
                ConnectionString = "mongodb://fake-connection",
                DatabaseName = "TestDb"
            });

            _httpClient = new HttpClient();

            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            _logger = loggerFactory.CreateLogger<AssetService>();

            var inMemorySettings = new Dictionary<string, string>();
            _configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings)
                .Build();

            _mockCollection = new Mock<IMongoCollection<AssetData>>();
            _mockDatabase = new Mock<IMongoDatabase>();
            _mockMongoClient = new Mock<IMongoClient>();

            _mockDatabase.Setup(db => db.GetCollection<AssetData>("coins", null))
                .Returns(_mockCollection.Object);

            _mockMongoClient.Setup(client => client.GetDatabase(_mongoDbSettings.Value.DatabaseName, null))
                .Returns(_mockDatabase.Object);
        }

        // Helper to setup FindAsync instead of FindSync
        private void SetupFindAsync(Mock<IMongoCollection<AssetData>> collectionMock, IEnumerable<AssetData> data)
        {
            var fakeCursor = new FakeAsyncCursor<AssetData>(data);
            collectionMock.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<AssetData>>(),
                It.IsAny<FindOptions<AssetData, AssetData>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeCursor);
        }

        [Fact]
        public async Task GetCryptoFromSymbolAsync_WithNullOrEmptySymbol_ReturnsNull()
        {
            var coinService = new AssetService(_mongoDbSettings, _mockMongoClient.Object, _logger);

            var resultEmpty = await coinService.GetFromSymbolAsync("");
            Assert.Null(resultEmpty);

            var resultNull = await coinService.GetFromSymbolAsync(null!);
            Assert.Null(resultNull);
        }

        [Fact]
        public async Task GetCryptoFromSymbolAsync_WhenCoinNotFound_ReturnsNull()
        {
            // Setup to return an empty list (simulate no coin found)
            SetupFindAsync(_mockCollection, new List<AssetData>());
            var coinService = new AssetService(_mongoDbSettings, _mockMongoClient.Object, _logger);
            string testSymbol = "BTCUSDT";

            var result = await coinService.GetFromSymbolAsync(testSymbol);

            Assert.Null(result);
            _mockCollection.Verify(x => x.FindAsync(
                It.IsAny<FilterDefinition<AssetData>>(),
                It.IsAny<FindOptions<AssetData, AssetData>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetCryptoFromSymbolAsync_WhenCoinFound_ReturnsCoinData()
        {
            var expectedCoin = new AssetData
            {
                _id = ObjectId.GenerateNewId(),
                Name = "Bitcoin",
                Symbol = "₿",
                Ticker = "BTC",
                Precision = 8,
                SubunitName = "Satoshi",
                CreateTime = DateTime.UtcNow
            };

            SetupFindAsync(_mockCollection, new List<AssetData> { expectedCoin });
            var coinService = new AssetService(_mongoDbSettings, _mockMongoClient.Object, _logger);
            string testSymbol = "BTCUSDT";

            var result = await coinService.GetFromSymbolAsync(testSymbol);

            Assert.NotNull(result);
            Assert.Equal(expectedCoin._id, result!._id);
            _mockCollection.Verify(x => x.FindAsync(
                It.IsAny<FilterDefinition<AssetData>>(),
                It.IsAny<FindOptions<AssetData, AssetData>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetCoinDataAsync_WhenCoinNotFound_ReturnsNull()
        {
            SetupFindAsync(_mockCollection, new List<AssetData>());
            var coinService = new AssetService(_mongoDbSettings, _mockMongoClient.Object, _logger);
            var coinId = ObjectId.GenerateNewId();

            var result = await coinService.GetByIdAsync(coinId);

            Assert.Null(result);
            _mockCollection.Verify(x => x.FindAsync(
                It.IsAny<FilterDefinition<AssetData>>(),
                It.IsAny<FindOptions<AssetData, AssetData>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetCoinDataAsync_WhenCoinFound_ReturnsCoinData()
        {
            var expectedCoin = new AssetData
            {
                _id = ObjectId.GenerateNewId(),
                Name = "Ethereum",
                Symbol = "Ξ",
                Ticker = "ETH",
                Precision = 18,
                SubunitName = "Gwei",
                CreateTime = DateTime.UtcNow
            };

            SetupFindAsync(_mockCollection, new List<AssetData> { expectedCoin });
            var coinService = new AssetService(_mongoDbSettings, _mockMongoClient.Object, _logger);
            var coinId = expectedCoin._id;

            var result = await coinService.GetByIdAsync(coinId);

            Assert.NotNull(result);
            Assert.Equal(expectedCoin._id, result!._id);
            _mockCollection.Verify(x => x.FindAsync(
                It.IsAny<FilterDefinition<AssetData>>(),
                It.IsAny<FindOptions<AssetData, AssetData>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
