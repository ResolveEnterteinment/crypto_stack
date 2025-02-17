using Application.Contracts.Responses.Exchange;  // Contains TestDataFactory and FakeAsyncCursor
using AspNetCore.Identity.MongoDbCore.Infrastructure;
using Binance.Net.Objects.Models.Spot;
using BinanceLibrary;
using Domain.DTOs;
using Domain.Models.Crypto;
using Domain.Models.Exchange;
using Domain.Models.Subscription;
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
    public class ExchangeServiceTests
    {
        // Shared options and dependencies for tests.
        private readonly IOptions<MongoDbSettings> _mongoDbSettings;
        private readonly IOptions<BinanceSettings> _binanceSettings;
        private readonly HttpClient _httpClient;
        private readonly ILogger<ExchangeService> _logger;
        private readonly IConfiguration _configuration;
        private readonly Mock<IMongoClient> _mockMongoClient;
        private readonly Mock<IMongoDatabase> _mockDatabase;
        private readonly Mock<IMongoCollection<ExchangeOrderData>> _mockExchangeOrderCollection;

        public ExchangeServiceTests()
        {
            _mongoDbSettings = Options.Create(new MongoDbSettings
            {
                ConnectionString = "mongodb://fake-connection",
                DatabaseName = "TestDb"
            });
            _binanceSettings = Options.Create(new BinanceSettings { ApiKey = "", ApiSecret = "", IsTestnet = true });
            _httpClient = new HttpClient();

            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            _logger = loggerFactory.CreateLogger<ExchangeService>();

            _configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>())
                .Build();

            _mockExchangeOrderCollection = new Mock<IMongoCollection<ExchangeOrderData>>();
            _mockDatabase = new Mock<IMongoDatabase>();
            _mockDatabase.Setup(db => db.GetCollection<ExchangeOrderData>("exchange_orders", null))
                .Returns(_mockExchangeOrderCollection.Object);

            _mockMongoClient = new Mock<IMongoClient>();
            _mockMongoClient.Setup(client => client.GetDatabase(_mongoDbSettings.Value.DatabaseName, null))
                .Returns(_mockDatabase.Object);
        }

        // Helper to setup IMongoCollection<CoinData>'s FindAsync using FakeAsyncCursor.
        private void SetupFindAsync(Mock<IMongoCollection<CoinData>> collectionMock, IEnumerable<CoinData> data)
        {
            var fakeCursor = new FakeAsyncCursor<CoinData>(data);
            collectionMock.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<CoinData>>(),
                It.IsAny<FindOptions<CoinData, CoinData>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(fakeCursor);
        }

        #region ProcessTransaction Tests

        [Fact]
        public async Task ProcessTransaction_ShouldReturnSuccessfulResponse()
        {
            // Arrange: Create default test data.
            var transaction = TestDataFactory.CreateDefaultTransactionData(); // NetAmount defaults to 100
            var coinAllocation = TestDataFactory.CreateDefaultCoinAllocation(); // 100% allocation by default
            var allocations = new List<CoinAllocation> { coinAllocation };
            var coinData = TestDataFactory.CreateDefaultCoinData(coinAllocation.CoinId);
            var fakeOrder = TestDataFactory.CreateDefaultOrder();

            // Setup mocks.
            var mockBinanceService = new Mock<IBinanceService>();
            mockBinanceService.Setup(x => x.PlaceSpotMarketBuyOrder("BTCUSDT", It.IsAny<decimal>()))
                              .ReturnsAsync(fakeOrder);

            var mockSubscriptionService = new Mock<ISubscriptionService>();
            mockSubscriptionService.Setup(x => x.GetCoinAllocationsAsync(transaction.SubscriptionId))
                                   .ReturnsAsync(allocations);

            var mockCoinService = new Mock<ICoinService>();
            mockCoinService.Setup(x => x.GetCoinDataAsync(coinAllocation.CoinId))
                           .ReturnsAsync(coinData);
            mockCoinService.Setup(x => x.GetCryptoFromSymbolAsync("BTCUSDT"))
                           .ReturnsAsync(coinData);

            _mockExchangeOrderCollection.Setup(x => x.InsertOneAsync(It.IsAny<ExchangeOrderData>(), null, default))
                                        .Returns(Task.CompletedTask);

            var exchangeService = new ExchangeService(
                _binanceSettings,
                _mongoDbSettings,
                _mockMongoClient.Object,
                mockBinanceService.Object,
                mockSubscriptionService.Object,
                mockCoinService.Object,
                _logger
            );

            // Act
            var responses = await exchangeService.ProcessTransaction(transaction);

            // Assert
            Assert.NotNull(responses);
            var responseList = new List<ExchangeOrderResponse>(responses);
            Assert.Single(responseList);
            var successResponse = responseList[0];
            Assert.True(successResponse.Success);
            Assert.Contains("Order created", successResponse.Message);

            // Verify that one order was inserted.
            _mockExchangeOrderCollection.Verify(x => x.InsertOneAsync(It.IsAny<ExchangeOrderData>(), null, default), Times.Once);
        }

        [Fact]
        public async Task ProcessTransaction_NetAmountNonPositive_ShouldThrowArgumentOutOfRangeException()
        {
            // Arrange: NetAmount is set to zero.
            var transaction = TestDataFactory.CreateDefaultTransactionData(netAmount: 0);
            var coinAllocation = TestDataFactory.CreateDefaultCoinAllocation();
            var allocations = new List<CoinAllocation> { coinAllocation };
            var coinData = TestDataFactory.CreateDefaultCoinData(coinAllocation.CoinId);
            var fakeOrder = TestDataFactory.CreateDefaultOrder();

            var mockBinanceService = new Mock<IBinanceService>();
            mockBinanceService.Setup(x => x.PlaceSpotMarketBuyOrder("BTCUSDT", It.IsAny<decimal>()))
                              .ReturnsAsync(fakeOrder);

            var mockSubscriptionService = new Mock<ISubscriptionService>();
            mockSubscriptionService.Setup(x => x.GetCoinAllocationsAsync(transaction.SubscriptionId))
                                   .ReturnsAsync(allocations);

            var mockCoinService = new Mock<ICoinService>();
            mockCoinService.Setup(x => x.GetCoinDataAsync(coinAllocation.CoinId))
                           .ReturnsAsync(coinData);
            mockCoinService.Setup(x => x.GetCryptoFromSymbolAsync("BTCUSDT"))
                           .ReturnsAsync(coinData);

            _mockExchangeOrderCollection.Setup(x => x.InsertOneAsync(It.IsAny<ExchangeOrderData>(), null, default))
                                        .Returns(Task.CompletedTask);

            var exchangeService = new ExchangeService(
                _binanceSettings,
                _mongoDbSettings,
                _mockMongoClient.Object,
                mockBinanceService.Object,
                mockSubscriptionService.Object,
                mockCoinService.Object,
                _logger
            );

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => exchangeService.ProcessTransaction(transaction));
            _mockExchangeOrderCollection.Verify(x => x.InsertOneAsync(It.IsAny<ExchangeOrderData>(), null, default), Times.Never);
        }

        [Fact]
        public async Task ProcessTransaction_WithEmptyAllocations_ShouldThrowKeyNotFoundException()
        {
            // Arrange: Setup empty allocations.
            var transaction = TestDataFactory.CreateDefaultTransactionData();
            var mockSubscriptionService = new Mock<ISubscriptionService>();
            mockSubscriptionService.Setup(x => x.GetCoinAllocationsAsync(transaction.SubscriptionId))
                                   .ReturnsAsync(new List<CoinAllocation>());

            var mockBinanceService = new Mock<IBinanceService>();
            var mockCoinService = new Mock<ICoinService>();

            _mockExchangeOrderCollection.Setup(x => x.InsertOneAsync(It.IsAny<ExchangeOrderData>(), null, default))
                                        .Returns(Task.CompletedTask);

            var exchangeService = new ExchangeService(
                _binanceSettings,
                _mongoDbSettings,
                _mockMongoClient.Object,
                mockBinanceService.Object,
                mockSubscriptionService.Object,
                mockCoinService.Object,
                _logger
            );

            // Act & Assert
            await Assert.ThrowsAsync<KeyNotFoundException>(() => exchangeService.ProcessTransaction(transaction));
            _mockExchangeOrderCollection.Verify(x => x.InsertOneAsync(It.IsAny<ExchangeOrderData>(), null, default), Times.Never);
        }

        [Fact]
        public async Task ProcessTransaction_AllocationPercentAmountOutOfRange_ShouldReturnFailureResponse()
        {
            // Arrange: Create a transaction with a valid NetAmount.
            var transaction = TestDataFactory.CreateDefaultTransactionData(netAmount: 100);

            // Create an allocation with PercentAmount set to 120 (out of range).
            var coinAllocation = TestDataFactory.CreateDefaultCoinAllocation(percentAmount: 120);
            var allocations = new List<CoinAllocation> { coinAllocation };

            // Create fake coin data (will not be used because the allocation is invalid).
            var coinData = TestDataFactory.CreateDefaultCoinData(coinAllocation.CoinId);
            var fakeOrder = TestDataFactory.CreateDefaultOrder();

            // Setup mocks for IBinanceService.
            var mockBinanceService = new Mock<IBinanceService>();
            mockBinanceService
                .Setup(x => x.PlaceSpotMarketBuyOrder("BTCUSDT", It.IsAny<decimal>()))
                .ReturnsAsync(fakeOrder);

            // Setup mock for ISubscriptionService.
            var mockSubscriptionService = new Mock<ISubscriptionService>();
            mockSubscriptionService
                .Setup(x => x.GetCoinAllocationsAsync(transaction.SubscriptionId))
                .ReturnsAsync(allocations);

            // Setup mocks for ICoinService.
            var mockCoinService = new Mock<ICoinService>();
            mockCoinService
                .Setup(x => x.GetCoinDataAsync(coinAllocation.CoinId))
                .ReturnsAsync(coinData);
            mockCoinService
                .Setup(x => x.GetCryptoFromSymbolAsync("BTCUSDT"))
                .ReturnsAsync(coinData);

            // Setup a mock IMongoCollection for ExchangeOrderData.
            var mockCollection = new Mock<IMongoCollection<ExchangeOrderData>>();
            mockCollection
                .Setup(x => x.InsertOneAsync(It.IsAny<ExchangeOrderData>(), null, default))
                .Returns(Task.CompletedTask);

            // Setup a mock IMongoDatabase that returns the mock collection.
            var mockDatabase = new Mock<IMongoDatabase>();
            mockDatabase
                .Setup(x => x.GetCollection<ExchangeOrderData>("exchange_orders", null))
                .Returns(mockCollection.Object);

            // Setup a mock IMongoClient that returns the mock database.
            var mockMongoClient = new Mock<IMongoClient>();
            mockMongoClient
                .Setup(x => x.GetDatabase(It.IsAny<string>(), null))
                .Returns(mockDatabase.Object);

            // Setup IOptions for BinanceSettings and MongoDbSettings.
            var binanceSettings = Options.Create(new BinanceSettings { ApiKey = "", ApiSecret = "", IsTestnet = true });
            var mongoDbSettings = Options.Create(new MongoDbSettings
            {
                ConnectionString = "mongodb://fake-connection",
                DatabaseName = "TestDb"
            });

            // Setup logger.
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var logger = loggerFactory.CreateLogger<ExchangeService>();

            // Create instance of ExchangeService using injected dependencies.
            var exchangeService = new ExchangeService(
                binanceSettings,
                mongoDbSettings,
                mockMongoClient.Object,
                mockBinanceService.Object,
                mockSubscriptionService.Object,
                mockCoinService.Object,
                logger
            );

            // Act
            var responses = await exchangeService.ProcessTransaction(transaction);

            // Assert
            Assert.NotNull(responses);
            var responseList = new List<ExchangeOrderResponse>(responses);

            // Expect a failure response since allocation is out of range.
            Assert.Single(responseList);
            var failureResponse = responseList[0];
            Assert.False(failureResponse.Success);
            Assert.Contains("Allocation must be a number between 0-100", failureResponse.Message);

            // Verify that no order was inserted.
            mockCollection.Verify(x => x.InsertOneAsync(It.IsAny<ExchangeOrderData>(), null, default), Times.Never);
        }


        [Fact]
        public async Task ProcessTransaction_WithMixedCoinData_ReturnsExpectedResponsesAndInsertsOneOrder()
        {
            // Arrange: Create a transaction and two allocations.
            var transaction = TestDataFactory.CreateDefaultTransactionData(netAmount: 100);
            var coinId1 = ObjectId.GenerateNewId();
            var coinId2 = ObjectId.GenerateNewId();
            // First allocation: 30%, returns null coin data.
            var coinAllocation1 = new CoinAllocation { CoinId = coinId1, PercentAmount = 30 };
            // Second allocation: 70%, returns valid coin data.
            var coinAllocation2 = new CoinAllocation { CoinId = coinId2, PercentAmount = 70 };
            var allocations = new List<CoinAllocation> { coinAllocation1, coinAllocation2 };

            // For first allocation, GetCoinDataAsync returns null.
            // For second allocation, return valid coin data.
            var coinData2 = TestDataFactory.CreateDefaultCoinData(coinId2);

            // Create a fake order for the second allocation.
            var fakeOrder = new BinancePlacedOrder
            {
                Id = 12345,
                ClientOrderId = "clientOrder123",
                Symbol = "BTCUSDT",
                Side = Binance.Net.Enums.OrderSide.Buy,
                Price = 97000m,
                QuoteQuantity = 70m, // 70% of 100 = 70
                CreateTime = DateTime.UtcNow,
                QuantityFilled = 0.001m,
                Status = Binance.Net.Enums.OrderStatus.Filled
            };

            var mockBinanceService = new Mock<IBinanceService>();
            mockBinanceService.Setup(x => x.PlaceSpotMarketBuyOrder("BTCUSDT", It.IsAny<decimal>()))
                              .ReturnsAsync(fakeOrder);

            var mockSubscriptionService = new Mock<ISubscriptionService>();
            mockSubscriptionService.Setup(x => x.GetCoinAllocationsAsync(transaction.SubscriptionId))
                                   .ReturnsAsync(allocations);

            var mockCoinService = new Mock<ICoinService>();
            // First allocation: return null.
            mockCoinService.Setup(x => x.GetCoinDataAsync(coinId1))
                           .ReturnsAsync((CoinData?)null);
            // Second allocation: return valid coin data.
            mockCoinService.Setup(x => x.GetCoinDataAsync(coinId2))
                           .ReturnsAsync(coinData2);
            mockCoinService.Setup(x => x.GetCryptoFromSymbolAsync("BTCUSDT"))
                           .ReturnsAsync(coinData2);

            _mockExchangeOrderCollection.Setup(x => x.InsertOneAsync(It.IsAny<ExchangeOrderData>(), null, default))
                                        .Returns(Task.CompletedTask);

            var exchangeService = new ExchangeService(
                _binanceSettings,
                _mongoDbSettings,
                _mockMongoClient.Object,
                mockBinanceService.Object,
                mockSubscriptionService.Object,
                mockCoinService.Object,
                _logger
            );

            // Act
            var responses = await exchangeService.ProcessTransaction(transaction);

            // Assert
            Assert.NotNull(responses);
            var responseList = new List<ExchangeOrderResponse>(responses);
            Assert.Equal(2, responseList.Count);

            // Instead of assuming order, find one failure and one success.
            var failureResponse = responseList.Find(r => !r.Success);
            var successResponse = responseList.Find(r => r.Success);

            Assert.NotNull(failureResponse);
            Assert.Contains("Coin data not found", failureResponse!.Message);

            Assert.NotNull(successResponse);
            Assert.Contains("Order created", successResponse!.Message);

            // Verify that InsertOneAsync was called exactly once.
            _mockExchangeOrderCollection.Verify(x => x.InsertOneAsync(It.IsAny<ExchangeOrderData>(), null, default), Times.Once);
        }
        #endregion
    }
}
