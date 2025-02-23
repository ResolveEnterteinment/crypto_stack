using AspNetCore.Identity.MongoDbCore.Infrastructure;
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
            var coinAllocations = new List<CoinAllocation>
            {
                new CoinAllocation { CoinId = ObjectId.GenerateNewId(), PercentAmount = 50 },
                new CoinAllocation { CoinId = ObjectId.GenerateNewId(), PercentAmount = 50 }
            };

            var subscriptionData = new SubscriptionData
            {
                _id = subscriptionId,
                CreateTime = DateTime.UtcNow,
                UserId = ObjectId.GenerateNewId(),
                Interval = "Monthly",
                Amount = 50,
                CoinAllocations = coinAllocations
            };

            SetupFindAsyncSubscription(_mockCollection, subscriptionData);

            var subscriptionService = new SubscriptionService(_mongoDbSettings, _mockMongoClient.Object, _logger);

            // Act
            var result = await subscriptionService.GetAllocationsAsync(subscriptionId);

            // Assert
            Assert.NotNull(result.Allocations);
            Assert.Equal(2, result.Allocations.Count);
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
            Assert.NotNull(result.Allocations);
            Assert.Empty(result.Allocations);
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
            Assert.NotNull(result.Allocations);
            Assert.Empty(result.Allocations);
        }
    }
}
