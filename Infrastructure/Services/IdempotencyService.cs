using Application.Interfaces;
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
    /// MongoDB-based implementation of the idempotency service with enhanced caching.
    /// </summary>
    public class IdempotencyService : BaseService<IdempotencyData>, IIdempotencyService
    {
        // Default expiration of 24 hours for idempotency records
        private static readonly TimeSpan DEFAULT_EXPIRATION = TimeSpan.FromHours(24);
        private static readonly TimeSpan CACHE_DURATION = TimeSpan.FromMinutes(30);
        private const string IDEMPOTENCY_CACHE_PREFIX = "idempotency:";

        private readonly JsonSerializerOptions _jsonOptions;

        public IdempotencyService(
            IOptions<MongoDbSettings> mongoDbSettings,
            IMongoClient mongoClient,
            ILogger<IdempotencyService> logger,
            IMemoryCache cache
        ) : base(
            mongoClient,
            mongoDbSettings,
            "idempotency",
            logger,
            cache,
            new List<CreateIndexModel<IdempotencyData>>
            {
                new CreateIndexModel<IdempotencyData>(
                    Builders<IdempotencyData>.IndexKeys.Ascending(x => x.Key),
                    new CreateIndexOptions { Name = "Key_Index", Unique = true }
                ),
                new CreateIndexModel<IdempotencyData>(
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
        }

        /// <summary>
        /// Gets the stored result for a given idempotency key with in-memory caching.
        /// </summary>
        public async Task<(bool exists, T result)> GetResultAsync<T>(string idempotencyKey)
        {
            if (string.IsNullOrEmpty(idempotencyKey))
                throw new ArgumentNullException(nameof(idempotencyKey), "Idempotency key cannot be null or empty.");

            // Check in-memory cache first for fast access
            string cacheKey = $"{IDEMPOTENCY_CACHE_PREFIX}{idempotencyKey}";

            if (_cache.TryGetValue(cacheKey, out T cachedResult))
            {
                _logger.LogDebug("Cache hit for idempotency key: {Key}", idempotencyKey);
                return (true, cachedResult);
            }

            try
            {
                // Not in cache, check database
                var filter = Builders<IdempotencyData>.Filter.Eq(r => r.Key, idempotencyKey);
                var record = await _collection.Find(filter).FirstOrDefaultAsync();

                if (record == null || string.IsNullOrEmpty(record.ResultJson))
                {
                    return (false, default);
                }

                try
                {
                    var result = JsonSerializer.Deserialize<T>(record.ResultJson, _jsonOptions);

                    // Store in cache for future fast access
                    _cache.Set(cacheKey, result, CACHE_DURATION);

                    return (true, result);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Failed to deserialize stored result for key {IdempotencyKey}", idempotencyKey);
                    return (false, default);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving idempotency record for key {Key}", idempotencyKey);
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
                var filter = Builders<IdempotencyData>.Filter.Eq(r => r.Key, idempotencyKey);
                var exists = await _collection.CountDocumentsAsync(filter) > 0;

                if (exists)
                {
                    // Update the existing record without changing the ID
                    var update = Builders<IdempotencyData>.Update
                        .Set(r => r.ResultJson, resultJson)
                        .Set(r => r.ExpiresAt, DateTime.UtcNow.Add(expiration ?? DEFAULT_EXPIRATION));

                    await _collection.UpdateOneAsync(filter, update);
                    _logger.LogDebug("Updated existing idempotency record for key {Key}", idempotencyKey);
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
                        ExpiresAt = DateTime.UtcNow.Add(expiration ?? DEFAULT_EXPIRATION)
                    };

                    await _collection.InsertOneAsync(record);
                    _logger.LogDebug("Created new idempotency record for key {Key}", idempotencyKey);
                }

                // Store in cache for future fast access
                if (result != null)
                {
                    string cacheKey = $"{IDEMPOTENCY_CACHE_PREFIX}{idempotencyKey}";
                    _cache.Set(cacheKey, result, CACHE_DURATION);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to store idempotency record for key {Key}", idempotencyKey);
                throw;
            }
        }

        /// <summary>
        /// Checks if an idempotency key already exists, using caching for performance.
        /// </summary>
        public async Task<bool> HasKeyAsync(string idempotencyKey)
        {
            if (string.IsNullOrEmpty(idempotencyKey))
                throw new ArgumentNullException(nameof(idempotencyKey), "Idempotency key cannot be null or empty.");

            // Fast path - check cache first
            string cacheKey = $"{IDEMPOTENCY_CACHE_PREFIX}exists:{idempotencyKey}";

            if (_cache.TryGetValue(cacheKey, out bool exists))
            {
                return exists;
            }

            try
            {
                // Check database
                var filter = Builders<IdempotencyData>.Filter.Eq(r => r.Key, idempotencyKey);
                var count = await _collection.CountDocumentsAsync(filter);
                exists = count > 0;

                // Cache the result
                _cache.Set(cacheKey, exists, CACHE_DURATION);

                return exists;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking for idempotency key {Key}", idempotencyKey);
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
                _logger.LogInformation("Found existing result for idempotency key: {Key}", idempotencyKey);
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
                _logger.LogError(ex, "Operation failed for idempotency key: {Key}", idempotencyKey);
                activity?.SetTag("error", true);
                activity?.SetTag("error.message", ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Removes an idempotency key and its associated result.
        /// </summary>
        public async Task<bool> RemoveKeyAsync(string idempotencyKey)
        {
            if (string.IsNullOrEmpty(idempotencyKey))
                throw new ArgumentNullException(nameof(idempotencyKey));

            try
            {
                var filter = Builders<IdempotencyData>.Filter.Eq(r => r.Key, idempotencyKey);
                var result = await _collection.DeleteOneAsync(filter);

                // Remove from cache
                string cacheKey = $"{IDEMPOTENCY_CACHE_PREFIX}{idempotencyKey}";
                _cache.Remove(cacheKey);

                string existsKey = $"{IDEMPOTENCY_CACHE_PREFIX}exists:{idempotencyKey}";
                _cache.Remove(existsKey);

                return result.DeletedCount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing idempotency key {Key}", idempotencyKey);
                return false;
            }
        }

        /// <summary>
        /// Purges expired idempotency records manually.
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