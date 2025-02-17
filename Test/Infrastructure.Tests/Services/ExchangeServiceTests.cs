using Application.Contracts.Responses.Exchange;
using AspNetCore.Identity.MongoDbCore.Infrastructure;
using Binance.Net.Objects.Models.Spot;
using BinanceLibrary;
using Domain.DTOs;
using Domain.Models.Crypto;
using Domain.Models.Exchange;
using Domain.Models.Subscription;
using Domain.Models.Transaction;
using Infrastructure.Services;
using Infrastructure.Tests.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;

namespace Infrastructure.Tests.Services
{
    public class ExchangeServiceTests
    {
        [Fact]
        public async Task ProcessTransaction_ShouldReturnSuccessfulResponse()
        {
            // Arrange

            // Create fake transaction data.
            var transaction = TestDataFactory.CreateDefaultTransactionData();
            var coinAllocation = TestDataFactory.CreateDefaultCoinAllocation();
            var allocations = new List<CoinAllocation> { coinAllocation };
            var coinData = TestDataFactory.CreateDefaultCoinData(coinAllocation.CoinId);

            // Create a fake BinancePlacedOrder (for a BUY order).
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
            var binanceSettings = Options.Create<BinanceSettings>(new BinanceSettings { ApiKey = "", ApiSecret = "", IsTestnet = true });
            var mongoDbSettings = Options.Create(new MongoDbSettings
            {
                ConnectionString = "mongodb://fake-connection",
                DatabaseName = "TestDb"
            });

            // Setup logger.
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var logger = loggerFactory.CreateLogger<ExchangeService>();

            // Create instance of ExchangeService using the injected dependencies.
            var exchangeService = new ExchangeService(
                binanceSettings,
                mongoDbSettings,
                mockMongoClient.Object,
                mockBinanceService.Object,  // Injecting the mock IBinanceService.
                mockSubscriptionService.Object,
                mockCoinService.Object,
                logger
            );

            // Act
            var responses = await exchangeService.ProcessTransaction(transaction);

            // Assert
            Assert.NotNull(responses);
            var responseList = new List<ExchangeOrderResponse>(responses);
            Assert.Single(responseList);
            Assert.True(responseList[0].Success);
            Assert.Contains("Order created", responseList[0].Message);

            // Verify that InsertOneAsync was called exactly once.
            mockCollection.Verify(x => x.InsertOneAsync(It.IsAny<ExchangeOrderData>(), null, default), Times.Once);
        }

        [Fact]
        public async Task ProcessTransaction_NetAmountNonPositive_ShouldReturnFailureResponse()
        {
            // Arrange
            // Create fake transaction data with NetAmount set to 0 (non-positive)
            var transaction = TestDataFactory.CreateDefaultTransactionData();
            transaction.NetAmount = 0;
            var coinAllocation = TestDataFactory.CreateDefaultCoinAllocation();
            var allocations = new List<CoinAllocation> { coinAllocation };
            var coinData = TestDataFactory.CreateDefaultCoinData(coinAllocation.CoinId);

            // Create a fake BinancePlacedOrder (for a BUY order).
            var fakeOrder = TestDataFactory.CreateDefaultOrder();

            // Setup mocks for IBinanceService.
            var mockBinanceService = new Mock<IBinanceService>();
            // In this test, the order creation should never be reached because quote order quantity is 0.
            // However, set up a default behavior.
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

            // Act & Assert
            // Expect a KeyNotFoundException when processing a transaction with no allocations.
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => exchangeService.ProcessTransaction(transaction));

            // Additionally, verify that no order insertion occurs.
            mockCollection.Verify(x => x.InsertOneAsync(It.IsAny<ExchangeOrderData>(), null, default), Times.Never);
        }

        [Fact]
        public async Task ProcessTransaction_AllocationPercentAmountOutOfRange_ShouldReturnFailureResponse()
        {
            // Arrange
            // Create fake transaction data with NetAmount set to 0 (non-positive)
            var transaction = TestDataFactory.CreateDefaultTransactionData();
            var coinAllocation = TestDataFactory.CreateDefaultCoinAllocation();
            coinAllocation.PercentAmount = 120;
            var allocations = new List<CoinAllocation> { coinAllocation };
            var coinData = TestDataFactory.CreateDefaultCoinData(coinAllocation.CoinId);

            // Create a fake BinancePlacedOrder (for a BUY order).
            var fakeOrder = TestDataFactory.CreateDefaultOrder();

            // Setup mocks for IBinanceService.
            var mockBinanceService = new Mock<IBinanceService>();
            // In this test, the order creation should never be reached because quote order quantity is 0.
            // However, set up a default behavior.
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
            // Expect at least one failure response since quoteOrderQuantity should be <= 0.
            Assert.Single(responseList);
            Assert.False(responseList[0].Success);
            Assert.Contains("Allocation must be a number between 0-100", responseList[0].Message);
            // Verify that no order was inserted since processing failed.
            mockCollection.Verify(x => x.InsertOneAsync(It.IsAny<ExchangeOrderData>(), null, default), Times.Never);
        }

        [Fact]
        public async Task ProcessTransaction_WhenNoAllocations_ShouldThrowKeyNotFoundException()
        {
            // Arrange

            // Create a fake transaction using TestDataFactory (or inline if preferred).
            var transaction = TestDataFactory.CreateDefaultTransactionData();
            // Setup ISubscriptionService to return an empty list.
            var mockSubscriptionService = new Mock<ISubscriptionService>();
            mockSubscriptionService
                .Setup(x => x.GetCoinAllocationsAsync(transaction.SubscriptionId))
                .ReturnsAsync(new List<CoinAllocation>());

            // Setup mocks for IBinanceService (though it should not be called in this scenario).
            var mockBinanceService = new Mock<IBinanceService>();

            // Setup mocks for ICoinService.
            var mockCoinService = new Mock<ICoinService>();

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

            // Create instance of ExchangeService using the injected dependencies.
            var exchangeService = new ExchangeService(
                binanceSettings,
                mongoDbSettings,
                mockMongoClient.Object,
                mockBinanceService.Object,
                mockSubscriptionService.Object,
                mockCoinService.Object,
                logger
            );

            // Act & Assert
            // Expect a KeyNotFoundException when processing a transaction with no allocations.
            await Assert.ThrowsAsync<KeyNotFoundException>(() => exchangeService.ProcessTransaction(transaction));

            // Additionally, verify that no order insertion occurs.
            mockCollection.Verify(x => x.InsertOneAsync(It.IsAny<ExchangeOrderData>(), null, default), Times.Never);
        }

        [Fact]
        public async Task ProcessTransaction_WithMixedCoinData_ReturnsExpectedResponsesAndInsertsOneOrder()
        {
            // Arrange

            // Create a fake transaction with a non-zero NetAmount.
            var transaction = new TransactionData
            {
                _id = ObjectId.GenerateNewId(),
                CreateTime = DateTime.UtcNow,
                UserId = ObjectId.GenerateNewId(),
                SubscriptionId = ObjectId.GenerateNewId(),
                PaymentProviderId = "pp1",
                TotalAmount = 103,
                PaymentProviderFee = 2m,
                PlatformFee = 1,
                NetAmount = 100, // Using 100 to keep math simple.
                Status = "Pending"
            };

            // Create two coin allocations: one 30% and the other 70%.
            var coinId1 = ObjectId.GenerateNewId();
            var coinId2 = ObjectId.GenerateNewId();
            var coinAllocation1 = new CoinAllocation
            {
                CoinId = coinId1,
                PercentAmount = 30
            };
            var coinAllocation2 = new CoinAllocation
            {
                CoinId = coinId2,
                PercentAmount = 70
            };
            var allocations = new List<CoinAllocation> { coinAllocation1, coinAllocation2 };

            // Create fake CoinData for the second allocation only.
            var coinData2 = new CoinData
            {
                _id = coinId2,
                CreateTime = DateTime.UtcNow,
                Name = "Bitcoin",
                Symbol = "₿",
                Ticker = "BTC",
                Precision = 8,
                SubunitName = "Satoshi"
            };

            // Create a fake BinancePlacedOrder (for a BUY order) for the second allocation.
            var fakeOrder = new BinancePlacedOrder
            {
                Id = 12345,
                ClientOrderId = "clientOrder123",
                Symbol = "BTCUSDT",
                Side = Binance.Net.Enums.OrderSide.Buy,
                Price = 97000m,
                QuoteQuantity = 70,  // 70% of 100 is 70
                CreateTime = DateTime.UtcNow,
                QuantityFilled = 0.001m,
                Status = Binance.Net.Enums.OrderStatus.Filled
            };

            // Setup mocks for IBinanceService.
            var mockBinanceService = new Mock<IBinanceService>();
            mockBinanceService
                .Setup(x => x.PlaceSpotMarketBuyOrder("BTCUSDT", It.IsAny<decimal>()))
                .ReturnsAsync(fakeOrder);
            // Optionally, setup Sell order if needed.

            // Setup mock for ISubscriptionService to return our two allocations.
            var mockSubscriptionService = new Mock<ISubscriptionService>();
            mockSubscriptionService
                .Setup(x => x.GetCoinAllocationsAsync(transaction.SubscriptionId))
                .ReturnsAsync(allocations);

            // Setup mocks for ICoinService:
            // For the first allocation, return null.
            // For the second allocation, return valid coinData.
            var mockCoinService = new Mock<ICoinService>();
            mockCoinService
                .Setup(x => x.GetCoinDataAsync(coinId1))
                .ReturnsAsync((CoinData?)null);
            mockCoinService
                .Setup(x => x.GetCoinDataAsync(coinId2))
                .ReturnsAsync(coinData2);
            // Also set up GetCryptoFromSymbolAsync so that when order.Symbol ("BTCUSDT") is queried, it returns coinData2.
            mockCoinService
                .Setup(x => x.GetCryptoFromSymbolAsync("BTCUSDT"))
                .ReturnsAsync(coinData2);

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

            // Create instance of ExchangeService using the injected dependencies.
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
            Assert.Equal(2, responseList.Count);

            // First response should be a failure (due to null coin data).
            Assert.False(responseList[0].Success);
            Assert.Contains("Coin data not found", responseList[0].Message);

            // Second response should be successful.
            Assert.True(responseList[1].Success);
            Assert.Contains("Order created", responseList[1].Message);

            // Verify that InsertOneAsync was called exactly once (only for the successful order).
            mockCollection.Verify(x => x.InsertOneAsync(It.IsAny<ExchangeOrderData>(), null, default), Times.Once);
        }
    }
}
