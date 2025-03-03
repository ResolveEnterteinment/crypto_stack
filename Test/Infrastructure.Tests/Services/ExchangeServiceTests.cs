using AspNetCore.Identity.MongoDbCore.Infrastructure;
using Binance.Net.Objects.Models.Spot;
using BinanceLibrary;
using Domain.Constants;
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
            _binanceSettings = Options.Create(new BinanceSettings { ApiKey = "123", ApiSecret = "456", IsTestnet = true });
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
        private static void SetupFindAsync(Mock<IMongoCollection<AssetData>> collectionMock, IEnumerable<AssetData> data)
        {
            var fakeCursor = new FakeAsyncCursor<AssetData>(data);
            collectionMock.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<AssetData>>(),
                It.IsAny<FindOptions<AssetData, AssetData>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(fakeCursor);
        }

        // Test subclass to override factory method
        public class MockExchangeService : ExchangeService
        {
            private readonly IBinanceService _testBinanceService;

            public MockExchangeService(
                IOptions<BinanceSettings> binanceSettings,
                IOptions<MongoDbSettings> mongoDbSettings,
                IMongoClient mongoClient,
                ISubscriptionService subscriptionService,
                IAssetService assetService,
                ILogger<ExchangeService> logger,
                IBinanceService testBinanceService)
                : base(binanceSettings, mongoDbSettings, mongoClient, subscriptionService, assetService, logger)
            {
                _testBinanceService = testBinanceService;
                CreateBinanceService(null, _testBinanceService);
            }
        }

        #region ProcessTransaction Tests

        [Fact]
        public async Task ProcessTransaction_ShouldReturnSuccessfulResponse()
        {
            // Arrange: Create default test data.
            var exchangeRequest = TestDataFactory.CreateDefaultExchangeRequest();
            var transactionData = TestDataFactory.CreateDefaultTransactionData(exchangeRequest);
            var coinAllocation = TestDataFactory.CreateDefaultCoinAllocation();
            var allocations = new List<AllocationData> { coinAllocation };
            var coinData = TestDataFactory.CreateDefaultCoinData(coinAllocation.AssetId);
            var fakeOrder = TestDataFactory.CreateDefaultOrder();

            var mockBinanceService = new Mock<IBinanceService>();
            mockBinanceService.Setup(x => x.PlaceSpotMarketBuyOrder("BTCUSDT", It.IsAny<decimal>(), transactionData.SubscriptionId))
                              .ReturnsAsync(fakeOrder);

            var mockSubscriptionService = new Mock<ISubscriptionService>();
            mockSubscriptionService.Setup(x => x.GetAllocationsAsync(transactionData.SubscriptionId))
                                   .ReturnsAsync(ResultWrapper<IReadOnlyCollection<AllocationData>>.Success(allocations));
            mockSubscriptionService.Setup(x => x.UpdateBalancesAsync(It.IsAny<ObjectId>(), It.IsAny<IEnumerable<ExchangeBalanceData>>()))
                                        .Returns(Task.FromResult(ResultWrapper<IReadOnlyCollection<ExchangeBalanceData>>.Success(new List<ExchangeBalanceData>())));

            var mockCoinService = new Mock<IAssetService>();
            mockCoinService.Setup(x => x.GetByIdAsync(coinAllocation.AssetId))
                           .ReturnsAsync(coinData);
            mockCoinService.Setup(x => x.GetFromSymbolAsync("BTCUSDT"))
                           .ReturnsAsync(coinData);

            _mockExchangeOrderCollection.Setup(x => x.InsertOneAsync(It.IsAny<ExchangeOrderData>(), null, default))
                                        .Returns(Task.CompletedTask);


            var mockExchangeService = new MockExchangeService(
                _binanceSettings,
                _mongoDbSettings,
                _mockMongoClient.Object,
                mockSubscriptionService.Object,
                mockCoinService.Object,
                _logger,
                mockBinanceService.Object
            );

            // Act
            var result = await mockExchangeService.ProcessTransaction(transactionData);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Single(result.Orders);
            Assert.Equal(1, result.SuccessfulOrders);
            Assert.True(result.Orders[0].IsSuccess);
            Assert.NotNull(result.Orders[0].OrderId);
            //Assert.Contains("xyz", result.Orders[0].ErrorMessage);

            // Verify that one order was inserted.
            _mockExchangeOrderCollection.Verify(x => x.InsertOneAsync(It.IsAny<ExchangeOrderData>(), null, default), Times.Once);
        }

        [Fact]
        public async Task ProcessTransaction_NetAmountNonPositive_ShouldReturnValidationError()
        {
            // Arrange: NetAmount is set to zero.
            var exchangeRequest = TestDataFactory.CreateDefaultExchangeRequest(netAmount: 0);
            var transactionData = TestDataFactory.CreateDefaultTransactionData(exchangeRequest);
            var coinAllocation = TestDataFactory.CreateDefaultCoinAllocation();
            var allocations = new List<AllocationData> { coinAllocation };
            var coinData = TestDataFactory.CreateDefaultCoinData(coinAllocation.AssetId);
            var fakeOrder = TestDataFactory.CreateDefaultOrder();

            var mockBinanceService = new Mock<IBinanceService>();
            mockBinanceService.Setup(x => x.PlaceSpotMarketBuyOrder("BTCUSDT", It.IsAny<decimal>(), transactionData.SubscriptionId))
                              .ReturnsAsync(fakeOrder);

            var mockSubscriptionService = new Mock<ISubscriptionService>();
            mockSubscriptionService.Setup(x => x.GetAllocationsAsync(transactionData.SubscriptionId))
                                   .ReturnsAsync(ResultWrapper<IReadOnlyCollection<AllocationData>>.Success(allocations));
            mockSubscriptionService.Setup(x => x.UpdateBalancesAsync(It.IsAny<ObjectId>(), It.IsAny<IEnumerable<ExchangeBalanceData>>()))
                                        .Returns(Task.FromResult(ResultWrapper<IReadOnlyCollection<ExchangeBalanceData>>.Success(new List<ExchangeBalanceData>())));

            var mockCoinService = new Mock<IAssetService>();
            mockCoinService.Setup(x => x.GetByIdAsync(coinAllocation.AssetId))
                           .ReturnsAsync(coinData);
            mockCoinService.Setup(x => x.GetFromSymbolAsync("BTCUSDT"))
                           .ReturnsAsync(coinData);

            _mockExchangeOrderCollection.Setup(x => x.InsertOneAsync(It.IsAny<ExchangeOrderData>(), null, default))
                                        .Returns(Task.CompletedTask);

            var mockExchangeService = new MockExchangeService(
                _binanceSettings,
                _mongoDbSettings,
                _mockMongoClient.Object,
                mockSubscriptionService.Object,
                mockCoinService.Object,
                _logger,
                mockBinanceService.Object
            );

            // Act & Assert
            var response = await mockExchangeService.ProcessTransaction(transactionData);
            Assert.False(response.IsSuccess);
            Assert.Single(response.Orders);
            Assert.Equal(FailureReason.ValidationError, response.Orders[0].FailureReason);
            Assert.Contains("Invalid transaction net amount.", response.Orders[0].ErrorMessage);
            _mockExchangeOrderCollection.Verify(x => x.InsertOneAsync(It.IsAny<ExchangeOrderData>(), null, default), Times.Never);
        }

        [Fact]
        public async Task ProcessTransaction_WithEmptyAllocations_ShouldReturnValidationError()
        {
            // Arrange: Setup empty allocations.
            var transactionRequest = TestDataFactory.CreateDefaultExchangeRequest();
            var transactionData = TestDataFactory.CreateDefaultTransactionData(transactionRequest);

            var mockSubscriptionService = new Mock<ISubscriptionService>();
            mockSubscriptionService.Setup(x => x.GetAllocationsAsync(transactionData.SubscriptionId))
                                   .ReturnsAsync(ResultWrapper<IReadOnlyCollection<AllocationData>>.Failure(FailureReason.ValidationError, "Subscription allocations can not be empty/null."));
            mockSubscriptionService.Setup(x => x.UpdateBalancesAsync(It.IsAny<ObjectId>(), It.IsAny<IEnumerable<ExchangeBalanceData>>()))
                                        .Returns(Task.FromResult(ResultWrapper<IReadOnlyCollection<ExchangeBalanceData>>.Success(new List<ExchangeBalanceData>())));

            var mockBinanceService = new Mock<IBinanceService>();
            var mockCoinService = new Mock<IAssetService>();

            _mockExchangeOrderCollection.Setup(x => x.InsertOneAsync(It.IsAny<ExchangeOrderData>(), null, default))
                                        .Returns(Task.CompletedTask);

            var exchangeService = new MockExchangeService(
                _binanceSettings,
                _mongoDbSettings,
                _mockMongoClient.Object,
                mockSubscriptionService.Object,
                mockCoinService.Object,
                _logger,
                mockBinanceService.Object
            );

            // Act & Assert
            var result = await exchangeService.ProcessTransaction(transactionData);
            Assert.False(result.IsSuccess);
            Assert.Single(result.Orders);
            Assert.Equal(FailureReason.ValidationError, result.Orders[0].FailureReason);
            Assert.Contains("Subscription allocations can not be empty/null.", result.Orders[0].ErrorMessage);
            _mockExchangeOrderCollection.Verify(x => x.InsertOneAsync(It.IsAny<ExchangeOrderData>(), null, default), Times.Never);
        }

        [Fact]
        public async Task ProcessTransaction_AllocationPercentAmountOutOfRange_ShouldReturnFailureResponse()
        {
            // Arrange: Create a transaction with a valid NetAmount.
            var exchangeRequest = TestDataFactory.CreateDefaultExchangeRequest(netAmount: 100);
            var transactionData = TestDataFactory.CreateDefaultTransactionData(exchangeRequest);

            // Create an allocation with PercentAmount set to 120 (out of range).
            var coinAllocation = TestDataFactory.CreateDefaultCoinAllocation(percentAmount: 120);
            var allocations = new List<AllocationData> { coinAllocation };

            // Create fake coin data (will not be used because the allocation is invalid).
            var coinData = TestDataFactory.CreateDefaultCoinData(coinAllocation.AssetId);
            var fakeOrder = TestDataFactory.CreateDefaultOrder();

            // Setup mocks for IBinanceService.
            var mockBinanceService = new Mock<IBinanceService>();
            mockBinanceService
                .Setup(x => x.PlaceSpotMarketBuyOrder("BTCUSDT", It.IsAny<decimal>(), transactionData.SubscriptionId))
                .ReturnsAsync(fakeOrder);

            // Setup mock for ISubscriptionService.
            var mockSubscriptionService = new Mock<ISubscriptionService>();
            mockSubscriptionService.Setup(x => x.GetAllocationsAsync(transactionData.SubscriptionId))
                                   .ReturnsAsync(ResultWrapper<IReadOnlyCollection<AllocationData>>.Success(allocations));
            mockSubscriptionService.Setup(x => x.UpdateBalancesAsync(It.IsAny<ObjectId>(), It.IsAny<IEnumerable<ExchangeBalanceData>>()))
                                        .Returns(Task.FromResult(ResultWrapper<IReadOnlyCollection<ExchangeBalanceData>>.Success(new List<ExchangeBalanceData>())));

            // Setup mocks for ICoinService.
            var mockCoinService = new Mock<IAssetService>();
            mockCoinService
                .Setup(x => x.GetByIdAsync(coinAllocation.AssetId))
                .ReturnsAsync(coinData);
            mockCoinService
                .Setup(x => x.GetFromSymbolAsync("BTCUSDT"))
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
            var exchangeService = new MockExchangeService(
                _binanceSettings,
                _mongoDbSettings,
                _mockMongoClient.Object,
                mockSubscriptionService.Object,
                mockCoinService.Object,
                _logger,
                mockBinanceService.Object
            );

            // Act
            var response = await exchangeService.ProcessTransaction(transactionData);

            // Assert
            Assert.NotNull(response);
            var result = new List<OrderResult>(response.Orders);

            // Expect a failure response since allocation is out of range.
            Assert.Single(result);
            var failureResponse = result[0];
            Assert.False(failureResponse.IsSuccess);
            Assert.Equal(FailureReason.ValidationError, failureResponse.FailureReason);
            Assert.Contains("Allocation must be between 0-100", failureResponse.ErrorMessage);

            // Verify that no order was inserted.
            mockCollection.Verify(x => x.InsertOneAsync(It.IsAny<ExchangeOrderData>(), null, default), Times.Never);
        }


        [Fact]
        public async Task ProcessTransaction_WithMixedCoinData_ReturnsExpectedResponsesAndInsertsOneOrder()
        {
            // Arrange: Create a transaction and two allocations.
            var exchangeRequest = TestDataFactory.CreateDefaultExchangeRequest(netAmount: 100);
            var transactionData = TestDataFactory.CreateDefaultTransactionData(exchangeRequest);
            var coinId1 = ObjectId.GenerateNewId();
            var coinId2 = ObjectId.GenerateNewId();
            // First allocation: 30%, returns null coin data.
            var coinAllocation1 = new AllocationData { AssetId = coinId1, PercentAmount = 30 };
            // Second allocation: 70%, returns valid coin data.
            var coinAllocation2 = new AllocationData { AssetId = coinId2, PercentAmount = 70 };
            var allocations = new List<AllocationData> { coinAllocation1, coinAllocation2 };

            // For first allocation, GetCoinDataAsync returns null.
            // For second allocation, return valid coin data.
            var coinData2 = TestDataFactory.CreateDefaultCoinData(coinId2);

            // Create a fake order for the second allocation.
            var fakePlacedOrder = new BinancePlacedOrder
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
            mockBinanceService.Setup(x => x.PlaceSpotMarketBuyOrder("BTCUSDT", It.IsAny<decimal>(), transactionData.SubscriptionId))
                              .ReturnsAsync(fakePlacedOrder);

            var mockSubscriptionService = new Mock<ISubscriptionService>();
            mockSubscriptionService.Setup(x => x.GetAllocationsAsync(transactionData.SubscriptionId))
                                   .ReturnsAsync(ResultWrapper<IReadOnlyCollection<AllocationData>>.Success(allocations));
            mockSubscriptionService.Setup(x => x.UpdateBalancesAsync(It.IsAny<ObjectId>(), It.IsAny<IEnumerable<ExchangeBalanceData>>()))
                                        .Returns(Task.FromResult(ResultWrapper<IReadOnlyCollection<ExchangeBalanceData>>.Success(new List<ExchangeBalanceData>())));

            var mockCoinService = new Mock<IAssetService>();
            // First allocation: return null.
            mockCoinService.Setup(x => x.GetByIdAsync(coinId1))
                           .ReturnsAsync((AssetData?)null);
            // Second allocation: return valid coin data.
            mockCoinService.Setup(x => x.GetByIdAsync(coinId2))
                           .ReturnsAsync(coinData2);
            mockCoinService.Setup(x => x.GetFromSymbolAsync("BTCUSDT"))
                           .ReturnsAsync(coinData2);

            _mockExchangeOrderCollection.Setup(x => x.InsertOneAsync(It.IsAny<ExchangeOrderData>(), null, default))
                                        .Returns(Task.CompletedTask);

            var exchangeService = new MockExchangeService(
                _binanceSettings,
                _mongoDbSettings,
                _mockMongoClient.Object,
                mockSubscriptionService.Object,
                mockCoinService.Object,
                _logger,
                mockBinanceService.Object
            );

            // Act
            var result = await exchangeService.ProcessTransaction(transactionData);
            _logger.LogInformation($"result: {result}");
            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal(2, result.TotalOrders);
            Assert.Equal(1, result.SuccessfulOrders);
            var responseList = result.Orders.ToList();

            // Instead of assuming order, find one failure and one success.
            var failureResponse = responseList.Find(r => !r.IsSuccess);
            var successResponse = responseList.Find(r => r.IsSuccess);

            Assert.NotNull(failureResponse);
            Assert.False(failureResponse.IsSuccess);
            Assert.Equal(FailureReason.DataNotFound, failureResponse.FailureReason);
            Assert.Contains("Coin data not found", failureResponse.ErrorMessage);

            Assert.NotNull(successResponse);
            Assert.True(successResponse.IsSuccess);
            Assert.NotNull(successResponse.OrderId);
            Assert.True(successResponse.IsInsertSuccess);
            Assert.True(successResponse.IsUpdateSuccess);

            // Verify that InsertOneAsync was called exactly once.
            _mockExchangeOrderCollection.Verify(x => x.InsertOneAsync(It.IsAny<ExchangeOrderData>(), null, default), Times.Once);
        }
        #endregion
    }
}
