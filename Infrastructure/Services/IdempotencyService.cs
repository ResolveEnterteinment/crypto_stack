using Domain.DTOs;
using Domain.Models.Idempotency;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using System.Text.Json;

namespace Infrastructure.Services
{
    /// <summary>
    /// Interface for idempotency operations
    /// </summary>
    public interface IIdempotencyService
    {
        Task<(bool exists, T result)> GetResultAsync<T>(string idempotencyKey);
        Task StoreResultAsync<T>(string idempotencyKey, T result);
        Task<bool> HasKeyAsync(string idempotencyKey);
        Task<T> ExecuteIdempotentOperationAsync<T>(string idempotencyKey, Func<Task<T>> operation, TimeSpan? expiration = null);
    }

    /// <summary>
    /// MongoDB-based implementation of the idempotency service
    /// </summary>
    public class IdempotencyService : BaseService<IdempotencyData>, IIdempotencyService
    {
        // Default expiration of 24 hours for idempotency records
        private static readonly TimeSpan DefaultExpiration = TimeSpan.FromHours(24);

        public IdempotencyService(
            IOptions<MongoDbSettings> mongoDbSettings,
            IMongoClient mongoClient,
            ILogger<IdempotencyService> logger)
            : base(mongoClient, mongoDbSettings, "idempotency", logger,
                Builders<IdempotencyData>.IndexKeys.Ascending(x => x.Key).Ascending(x => x.ExpiresAt))
        {
            // Create TTL index on ExpiresAt field
            var indexModel = new CreateIndexModel<IdempotencyData>(
                Builders<IdempotencyData>.IndexKeys.Ascending(x => x.ExpiresAt),
                new CreateIndexOptions { ExpireAfter = TimeSpan.Zero }
            );

            _collection.Indexes.CreateOne(indexModel);
        }

        /// <summary>
        /// Gets the stored result for a given idempotency key
        /// </summary>
        public async Task<(bool exists, T result)> GetResultAsync<T>(string idempotencyKey)
        {
            if (string.IsNullOrEmpty(idempotencyKey))
                throw new ArgumentNullException(nameof(idempotencyKey));

            var record = await _collection.Find(r => r.Key == idempotencyKey).FirstOrDefaultAsync();
            if (record == null)
                return (false, default);

            if (string.IsNullOrEmpty(record.ResultJson))
                return (false, default);

            return (true, JsonSerializer.Deserialize<T>(record.ResultJson));
        }

        /// <summary>
        /// Stores a result with the given idempotency key
        /// </summary>
        public async Task StoreResultAsync<T>(string idempotencyKey, T result)
        {
            if (string.IsNullOrEmpty(idempotencyKey))
                throw new ArgumentNullException(nameof(idempotencyKey));

            string resultJson = null;
            if (result != null)
            {
                resultJson = JsonSerializer.Serialize(result);
            }

            var record = new IdempotencyData
            {
                Id = Guid.NewGuid(), // Always generate a new GUID for the document
                Key = idempotencyKey,
                ResultJson = resultJson,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.Add(DefaultExpiration)
            };

            var filter = Builders<IdempotencyData>.Filter.Eq(r => r.Key, idempotencyKey);
            var options = new ReplaceOptions { IsUpsert = true };

            await _collection.ReplaceOneAsync(filter, record, options);
        }

        /// <summary>
        /// Checks if an idempotency key already exists
        /// </summary>
        public async Task<bool> HasKeyAsync(string idempotencyKey)
        {
            if (string.IsNullOrEmpty(idempotencyKey))
                throw new ArgumentNullException(nameof(idempotencyKey));

            var count = await _collection.CountDocumentsAsync(r => r.Key == idempotencyKey);
            return count > 0;
        }

        /// <summary>
        /// Executes an operation with idempotency guarantees
        /// </summary>
        public async Task<T> ExecuteIdempotentOperationAsync<T>(
            string idempotencyKey,
            Func<Task<T>> operation,
            TimeSpan? expiration = null)
        {
            if (string.IsNullOrEmpty(idempotencyKey))
                throw new ArgumentNullException(nameof(idempotencyKey));

            // Check if the operation was already executed
            var (resultExists, existingResult) = await GetResultAsync<T>(idempotencyKey);
            if (resultExists)
            {
                _logger.LogInformation("Found existing result for idempotency key: {IdempotencyKey}", idempotencyKey);
                return existingResult;
            }

            // Execute the operation
            var result = await operation();

            // Store the result for future idempotency checks
            var record = new IdempotencyData
            {
                Key = idempotencyKey,
                ResultJson = JsonSerializer.Serialize(result),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.Add(expiration ?? DefaultExpiration)
            };

            await InsertOneAsync(record);

            return result;
        }
    }
}