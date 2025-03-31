using Domain.DTOs.Settings;
using Domain.Models.Idempotency;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using System.Diagnostics;
using System.Text.Json;

namespace Infrastructure.Services
{
    /// <summary>
    /// Interface for idempotency operations to ensure operations are executed exactly once.
    /// </summary>
    public interface IIdempotencyService
    {
        /// <summary>
        /// Gets the result of a previously executed operation by its idempotency key.
        /// </summary>
        /// <typeparam name="T">The return type of the operation.</typeparam>
        /// <param name="idempotencyKey">The unique key identifying the operation.</param>
        /// <returns>A tuple containing whether the result exists and the result itself if it exists.</returns>
        Task<(bool exists, T result)> GetResultAsync<T>(string idempotencyKey);

        /// <summary>
        /// Stores the result of an operation with the given idempotency key.
        /// </summary>
        /// <typeparam name="T">The type of the result.</typeparam>
        /// <param name="idempotencyKey">The unique key identifying the operation.</param>
        /// <param name="result">The result to store.</param>
        /// <param name="expiration">Optional custom expiration timespan for this record.</param>
        Task StoreResultAsync<T>(string idempotencyKey, T result, TimeSpan? expiration = null);

        /// <summary>
        /// Checks if an operation with the given idempotency key has been executed.
        /// </summary>
        /// <param name="idempotencyKey">The unique key identifying the operation.</param>
        /// <returns>True if the operation has been executed, otherwise false.</returns>
        Task<bool> HasKeyAsync(string idempotencyKey);

        /// <summary>
        /// Executes an operation with idempotency guarantees.
        /// </summary>
        /// <typeparam name="T">The return type of the operation.</typeparam>
        /// <param name="idempotencyKey">The unique key identifying the operation.</param>
        /// <param name="operation">The operation to execute.</param>
        /// <param name="expiration">Optional custom expiration timespan for this record.</param>
        /// <returns>The result of the operation.</returns>
        Task<T> ExecuteIdempotentOperationAsync<T>(string idempotencyKey, Func<Task<T>> operation, TimeSpan? expiration = null);
    }

    /// <summary>
    /// MongoDB-based implementation of the idempotency service to ensure operations are executed exactly once.
    /// </summary>
    public class IdempotencyService : BaseService<IdempotencyData>, IIdempotencyService
    {
        // Default expiration of 24 hours for idempotency records
        private static readonly TimeSpan DefaultExpiration = TimeSpan.FromHours(24);
        private readonly JsonSerializerOptions _jsonOptions;

        public IdempotencyService(
            IOptions<MongoDbSettings> mongoDbSettings,
            IMongoClient mongoClient,
            ILogger<IdempotencyService> logger,
            IMemoryCache cache
            )
            : base(
                  mongoClient,
                  mongoDbSettings,
                  "idempotency",
                  logger,
                  cache,
                  new List<CreateIndexModel<IdempotencyData>>
                    {
                        new (
                            Builders<IdempotencyData>.IndexKeys.Ascending(x => x.Key),
                            new CreateIndexOptions { Name = "Key_Index" }
                        ),
                        new (
                            Builders<IdempotencyData>.IndexKeys.Ascending(x => x.ExpiresAt),
                            new CreateIndexOptions { Name = "ExpiresAt_1", ExpireAfter = TimeSpan.Zero }
                        )
                    }
                  )
        {
            // Setup json serialization options
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };

            // Create TTL index on ExpiresAt field to auto-clean expired records
            CreateTtlIndex();
        }

        /// <summary>
        /// Creates a TTL (Time-To-Live) index on the ExpiresAt field to automatically remove expired records.
        /// </summary>
        private void CreateTtlIndex()
        {
            try
            {
                var indexModel = new CreateIndexModel<IdempotencyData>(
                    Builders<IdempotencyData>.IndexKeys.Ascending(x => x.ExpiresAt),
                    new CreateIndexOptions { ExpireAfter = TimeSpan.Zero, Name = "TTL_ExpiresAt" }
                );

                _collection.Indexes.CreateOne(indexModel);
                _logger.LogInformation("Created TTL index on idempotency collection");
            }
            catch (MongoCommandException ex) when (ex.Message.Contains("already exists"))
            {
                // Index already exists, this is fine
                _logger.LogDebug("TTL index already exists on idempotency collection");
            }
            catch (Exception ex)
            {
                // Log but don't fail initialization
                _logger.LogWarning(ex, "Failed to create TTL index on idempotency collection");
            }
        }

        /// <summary>
        /// Gets the stored result for a given idempotency key.
        /// </summary>
        public async Task<(bool exists, T result)> GetResultAsync<T>(string idempotencyKey)
        {
            if (string.IsNullOrEmpty(idempotencyKey))
                throw new ArgumentNullException(nameof(idempotencyKey), "Idempotency key cannot be null or empty.");

            var record = await _collection.Find(r => r.Key == idempotencyKey).FirstOrDefaultAsync();
            if (record == null || string.IsNullOrEmpty(record.ResultJson))
                return (false, default);

            try
            {
                var result = JsonSerializer.Deserialize<T>(record.ResultJson, _jsonOptions);
                return (true, result);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to deserialize stored result for key {IdempotencyKey}", idempotencyKey);
                return (false, default);
            }
        }

        /// <summary>
        /// Stores a result with the given idempotency key.
        /// </summary>
        public async Task StoreResultAsync<T>(string idempotencyKey, T result, TimeSpan? expiration = null)
        {
            if (string.IsNullOrEmpty(idempotencyKey))
                throw new ArgumentNullException(nameof(idempotencyKey), "Idempotency key cannot be null or empty.");

            string resultJson = null;
            if (result != null)
            {
                try
                {
                    resultJson = JsonSerializer.Serialize(result, _jsonOptions);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Failed to serialize result for key {IdempotencyKey}", idempotencyKey);
                    throw;
                }
            }

            try
            {
                // First check if a record already exists
                var existingRecord = await _collection.Find(r => r.Key == idempotencyKey).FirstOrDefaultAsync();

                if (existingRecord != null)
                {
                    // Update the existing record without changing the _id
                    var updateDef = Builders<IdempotencyData>.Update
                        .Set(r => r.ResultJson, resultJson)
                        .Set(r => r.ExpiresAt, DateTime.UtcNow.Add(expiration ?? DefaultExpiration));

                    await _collection.UpdateOneAsync(r => r.Key == idempotencyKey, updateDef);
                }
                else
                {
                    // Create a new record
                    var record = new IdempotencyData
                    {
                        Id = Guid.NewGuid(),
                        Key = idempotencyKey,
                        ResultJson = resultJson,
                        CreatedAt = DateTime.UtcNow,
                        ExpiresAt = DateTime.UtcNow.Add(expiration ?? DefaultExpiration)
                    };

                    await _collection.InsertOneAsync(record);
                }

                _logger.LogDebug("Stored idempotency record for key {IdempotencyKey}", idempotencyKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to store idempotency record for key {IdempotencyKey}", idempotencyKey);
                throw;
            }
        }

        /// <summary>
        /// Checks if an idempotency key already exists.
        /// </summary>
        public async Task<bool> HasKeyAsync(string idempotencyKey)
        {
            if (string.IsNullOrEmpty(idempotencyKey))
                throw new ArgumentNullException(nameof(idempotencyKey), "Idempotency key cannot be null or empty.");

            try
            {
                var count = await _collection.CountDocumentsAsync(r => r.Key == idempotencyKey);
                return count > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking for idempotency key {IdempotencyKey}", idempotencyKey);
                // In case of error, safer to return false so operation can proceed
                return false;
            }
        }

        /// <summary>
        /// Executes an operation with idempotency guarantees.
        /// </summary>
        public async Task<T> ExecuteIdempotentOperationAsync<T>(
            string idempotencyKey,
            Func<Task<T>> operation,
            TimeSpan? expiration = null)
        {
            if (string.IsNullOrEmpty(idempotencyKey))
                throw new ArgumentNullException(nameof(idempotencyKey), "Idempotency key cannot be null or empty.");

            using var activity = new Activity("IdempotentOperation")
                .SetTag("idempotency.key", idempotencyKey)
                .Start();

            // Check if the operation was already executed
            var (resultExists, existingResult) = await GetResultAsync<T>(idempotencyKey);
            if (resultExists)
            {
                _logger.LogInformation("Found existing result for idempotency key: {IdempotencyKey}", idempotencyKey);
                activity?.SetTag("idempotency.cached", true);
                return existingResult;
            }

            activity?.SetTag("idempotency.cached", false);

            try
            {
                // Execute the operation
                var result = await operation();

                // Store the result for future idempotency checks
                await StoreResultAsync(idempotencyKey, result, expiration);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Operation failed for idempotency key: {IdempotencyKey}", idempotencyKey);
                activity?.SetTag("error", true);
                activity?.SetTag("error.message", ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Removes an idempotency key and its associated result.
        /// Useful for testing or administrative cleanup.
        /// </summary>
        public async Task<bool> RemoveKeyAsync(string idempotencyKey)
        {
            if (string.IsNullOrEmpty(idempotencyKey))
                throw new ArgumentNullException(nameof(idempotencyKey));

            try
            {
                var result = await _collection.DeleteOneAsync(r => r.Key == idempotencyKey);
                return result.DeletedCount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing idempotency key {IdempotencyKey}", idempotencyKey);
                return false;
            }
        }

        /// <summary>
        /// Purges expired idempotency records manually if needed.
        /// Normally this would be handled by MongoDB's TTL index.
        /// </summary>
        public async Task<long> PurgeExpiredRecordsAsync()
        {
            try
            {
                var filter = Builders<IdempotencyData>.Filter.Lt(r => r.ExpiresAt, DateTime.UtcNow);
                var result = await _collection.DeleteManyAsync(filter);
                _logger.LogInformation("Purged {Count} expired idempotency records", result.DeletedCount);
                return result.DeletedCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error purging expired idempotency records");
                return 0;
            }
        }
    }
}