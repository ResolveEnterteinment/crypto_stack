using AspNetCore.Identity.MongoDbCore.Infrastructure;
using Domain.Constants;
using Domain.Models.Balance;
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
    public class SubscriptionServiceTests
    {
        private readonly IOptions<MongoDbSettings> _mongoDbSettings;
        private readonly Mock<IMongoClient> _mockMongoClient;
        private readonly Mock<IMongoDatabase> _mockDatabase;
        private readonly Mock<IMongoCollection<SubscriptionData>> _mockCollection;
        private readonly ILogger<SubscriptionService> _logger;
        private readonly IConfiguration _configuration;

        public SubscriptionServiceTests()
        {
            _mongoDbSettings = Options.Create(new MongoDbSettings
            {
                ConnectionString = "mongodb://fake-connection",
                DatabaseName = "TestDb"
            });

            _mockCollection = new Mock<IMongoCollection<SubscriptionData>>();
            _mockDatabase = new Mock<IMongoDatabase>();
            _mockDatabase.Setup(db => db.GetCollection<SubscriptionData>("subscriptions", null))
                .Returns(_mockCollection.Object);
            _mockMongoClient = new Mock<IMongoClient>();
            _mockMongoClient.Setup(client => client.GetDatabase(_mongoDbSettings.Value.DatabaseName, null))
                .Returns(_mockDatabase.Object);

            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            _logger = loggerFactory.CreateLogger<SubscriptionService>();

            _configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>())
                .Build();
        }

        // Helper method to simulate FindAsync behavior for SubscriptionData.
        private void SetupFindAsyncSubscription(Mock<IMongoCollection<SubscriptionData>> collectionMock, SubscriptionData? subscription)
        {
            IEnumerable<SubscriptionData> data = subscription != null ? new List<SubscriptionData> { subscription } : new List<SubscriptionData>();
            var fakeCursor = new FakeAsyncCursor<SubscriptionData>(data);

            collectionMock.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<SubscriptionData>>(),
                It.IsAny<FindOptions<SubscriptionData, SubscriptionData>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(fakeCursor);
        }

        [Fact]
        public async Task GetCoinAllocationsAsync_WhenSubscriptionExists_ReturnsAllocations()
        {
            // Arrange: Create a SubscriptionData with a list of coin allocations.
            var subscriptionId = ObjectId.GenerateNewId();
            var coinAllocations = new List<AllocationData>
            {
                new AllocationData { AssetId = ObjectId.GenerateNewId(), PercentAmount = 50 },
                new AllocationData { AssetId = ObjectId.GenerateNewId(), PercentAmount = 50 }
            };

            var subscriptionData = new SubscriptionData
            {
                _id = subscriptionId,
                CreateTime = DateTime.UtcNow,
                UserId = ObjectId.GenerateNewId(),
                Interval = "Monthly",
                Amount = 50,
                Allocations = coinAllocations
            };

            SetupFindAsyncSubscription(_mockCollection, subscriptionData);

            var subscriptionService = new SubscriptionService(_mongoDbSettings, _mockMongoClient.Object, _logger);

            // Act
            var result = await subscriptionService.GetAllocationsAsync(subscriptionId);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Data);
            Assert.Equal(2, result.Data.Count);
        }

        [Fact]
        public async Task GetCoinAllocationsAsync_WhenSubscriptionNotFound_ReturnsEmptyList()
        {
            // Arrange: Simulate subscription not found (i.e. find returns empty).
            var subscriptionId = ObjectId.GenerateNewId();
            SetupFindAsyncSubscription(_mockCollection, null);

            var subscriptionService = new SubscriptionService(_mongoDbSettings, _mockMongoClient.Object, _logger);

            // Act
            var result = await subscriptionService.GetAllocationsAsync(subscriptionId);

            // Assert: The catch block should return an empty list.
            Assert.False(result.IsSuccess);
            Assert.NotNull(result.Data);
            Assert.Empty(result.Data);
        }

        [Fact]
        public async Task GetCoinAllocationsAsync_WhenFindThrowsException_ReturnsEmptyList()
        {
            // Arrange: Simulate an exception during find.
            var subscriptionId = ObjectId.GenerateNewId();
            _mockCollection.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<SubscriptionData>>(),
                It.IsAny<FindOptions<SubscriptionData, SubscriptionData>>(),
                It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Database error"));

            var subscriptionService = new SubscriptionService(_mongoDbSettings, _mockMongoClient.Object, _logger);

            // Act
            var result = await subscriptionService.GetAllocationsAsync(subscriptionId);

            // Assert: In case of exception, an empty list is returned.
            Assert.False(result.IsSuccess);
            Assert.Equal(FailureReason.DatabaseError, result.FailureReason);
            Assert.NotNull(result.Data);
            Assert.Empty(result.Data);
        }
        [Fact]
        public async Task GetUserSubscriptionsAsync_WhenUserExists_ReturnsSubscriptions()
        {
            // Arrange: Create a SubscriptionData with a list of coin allocations.
            var userId = ObjectId.GenerateNewId();
            var coinAllocations = new List<AllocationData>
            {
                new AllocationData { AssetId = ObjectId.GenerateNewId(), PercentAmount = 60 },
                new AllocationData { AssetId = ObjectId.GenerateNewId(), PercentAmount = 40 }
            };

            var subscriptionData = new SubscriptionData
            {
                UserId = userId,
                Interval = "Monthly",
                Amount = 100,
                Allocations = coinAllocations
            };

            SetupFindAsyncSubscription(_mockCollection, subscriptionData);

            var subscriptionService = new SubscriptionService(_mongoDbSettings, _mockMongoClient.Object, _logger);

            // Act
            var result = await subscriptionService.GetUserSubscriptionsAsync(userId);
            var subscriptionsList = result.ToList();
            // Assert
            Assert.NotNull(subscriptionsList);
            Assert.Single(subscriptionsList);
            Assert.Equal(100, subscriptionsList[0].Amount);
            Assert.Equal(2, subscriptionsList[0].Allocations.Count());
        }
        [Fact]
        public async Task GetUserSubscriptionsAsync_WhenNoSubscriptions_ReturnsEmptyList()
        {
            // Arrange: Create a SubscriptionData with a list of coin allocations.
            var userId = ObjectId.GenerateNewId();

            SetupFindAsyncSubscription(_mockCollection, default);

            var subscriptionService = new SubscriptionService(_mongoDbSettings, _mockMongoClient.Object, _logger);

            // Act
            var result = await subscriptionService.GetUserSubscriptionsAsync(userId);
            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }
        [Fact]
        public async Task UpdateBalancesAsync_WhenUserSubscriptionExists_ReturnsUpdateBalanceResult()
        {
            // Arrange: Create a SubscriptionData with a list of coin allocations.
            var userId = ObjectId.GenerateNewId();
            var coinAllocations = new List<AllocationData>
            {
                new AllocationData { AssetId = ObjectId.GenerateNewId(), PercentAmount = 60 },
                new AllocationData { AssetId = ObjectId.GenerateNewId(), PercentAmount = 40 }
            };

            var subscriptionData = new SubscriptionData
            {
                UserId = userId,
                Interval = "Monthly",
                Amount = 100,
                Allocations = coinAllocations
            };

            var updateBalances = new List<BalanceData>()
            {
                new BalanceData {
                    CoinId = coinAllocations[0].AssetId,
                    Total = 0.003m
                },
                new BalanceData {
                    CoinId = coinAllocations[1].AssetId,
                    Total = 1.7m
                }
            };

            SetupFindAsyncSubscription(_mockCollection, subscriptionData);

            var subscriptionService = new SubscriptionService(_mongoDbSettings, _mockMongoClient.Object, _logger);

            // Act
            var result = await subscriptionService.UpdateBalancesAsync(subscriptionData._id, updateBalances);
            // Assert
            Assert.NotNull(result);
            Assert.True(result.IsSuccess);
            Assert.Equal(2, result.Data.Count);
        }
        [Fact]
        public async Task UpdateBalancesAsync_WhenNoUserSubscription_ReturnsFailedUpdateBalanceResult()
        {
            // Arrange: Create a SubscriptionData with a list of coin allocations.
            var userId = ObjectId.GenerateNewId();
            var coinAllocations = new List<AllocationData>
            {
                new AllocationData { AssetId = ObjectId.GenerateNewId(), PercentAmount = 60 },
                new AllocationData { AssetId = ObjectId.GenerateNewId(), PercentAmount = 40 }
            };

            var subscriptionData = new SubscriptionData
            {
                UserId = userId,
                Interval = "Monthly",
                Amount = 100,
                Allocations = coinAllocations,
                Balances = new List<ExchangeBalanceData>()
                {
                    new ExchangeBalanceData
                    {
                        CoinId = coinAllocations[0].AssetId,
                        Total = 0
                    },
                    new ExchangeBalanceData
                    {
                        CoinId = coinAllocations[1].AssetId,
                        Total = 0
                    },
                }
            };

            var updateBalances = new List<ExchangeBalanceData>()
            {
                new ExchangeBalanceData {
                    CoinId = coinAllocations[0].AssetId,
                    Total = 0.003m
                },
                new ExchangeBalanceData {
                    CoinId = coinAllocations[1].AssetId,
                    Total = 1.7m
                }
            };

            SetupFindAsyncSubscription(_mockCollection, subscriptionData);

            var subscriptionService = new SubscriptionService(_mongoDbSettings, _mockMongoClient.Object, _logger);

            // Act
            var result = await subscriptionService.UpdateBalancesAsync(ObjectId.GenerateNewId(), updateBalances);
            // Assert
            Assert.NotNull(result);
            Assert.False(result.IsSuccess);
            Assert.Empty(result.Data);
            Assert.Contains("Unable to update subscription balances", result.ErrorMessage);
        }
    }
}
