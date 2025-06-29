﻿using Application.Interfaces;
using Application.Interfaces.Base;
using Application.Interfaces.Logging;
using Domain.DTOs;
using Domain.Models.Idempotency;
using Infrastructure.Services.Base;
using Microsoft.Extensions.Caching.Memory;
using MongoDB.Driver;
using System.Diagnostics;
using System.Text.Json;

namespace Infrastructure.Services
{
    public class IdempotencyService : BaseService<IdempotencyData>, IIdempotencyService
    {
        private static readonly TimeSpan DEFAULT_EXPIRATION = TimeSpan.FromHours(24);
        private static readonly TimeSpan CACHE_DURATION = TimeSpan.FromMinutes(30);
        private const string CACHE_PREFIX = "idempotency:";

        private readonly IMemoryCache _memoryCache;
        private readonly JsonSerializerOptions _jsonOptions;

        public IdempotencyService(
            ICrudRepository<IdempotencyData> repository,
            ICacheService<IdempotencyData> cacheService,
            IMongoIndexService<IdempotencyData> indexService,
            ILoggingService logger,
            IEventService eventService,
            IMemoryCache memoryCache
        ) : base(
            repository,
            cacheService,
            indexService,
            logger,
            eventService,
            new[]
            {
                new CreateIndexModel<IdempotencyData>(
                    Builders<IdempotencyData>.IndexKeys.Ascending(d => d.Key),
                    new CreateIndexOptions { Name = "Key_Index", Unique = true }),
                new CreateIndexModel<IdempotencyData>(
                    Builders<IdempotencyData>.IndexKeys.Ascending(d => d.ExpiresAt),
                    new CreateIndexOptions { Name = "ExpiresAt_1", ExpireAfter = TimeSpan.Zero })
            }
        )
        {
            _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };
        }

        public async Task<(bool exists, T result)> GetResultAsync<T>(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentNullException(nameof(key));
            }

            string cacheKey = CACHE_PREFIX + key;
            if (_memoryCache.TryGetValue(cacheKey, out T cached))
            {
                return (true, cached);
            }

            // Fetch from DB
            var filter = Builders<IdempotencyData>.Filter.Eq(d => d.Key, key);
            var record = await _repository.GetOneAsync(filter);
            if (record == null || string.IsNullOrEmpty(record.ResultJson))
            {
                return (false, default);
            }

            try
            {
                var result = JsonSerializer.Deserialize<T>(record.ResultJson, _jsonOptions);
                _ = _memoryCache.Set(cacheKey, result, CACHE_DURATION);
                return (true, result);
            }
            catch (JsonException)
            {
                Logger.LogError("Deserialization failed for idempotency key {Key}", key);
                return (false, default);
            }
        }

        public async Task StoreResultAsync<T>(string key, T result, TimeSpan? expiration = null)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentNullException(nameof(key));
            }

            var json = result != null
                ? JsonSerializer.Serialize(result, _jsonOptions)
                : null;

            var filter = Builders<IdempotencyData>.Filter.Eq(d => d.Key, key);
            var existing = await _repository.GetOneAsync(filter);
            var expireAt = DateTime.UtcNow.Add(expiration ?? DEFAULT_EXPIRATION);

            if (existing != null)
            {
                // Update
                _ = await _repository.UpdateAsync(existing.Id, new { ResultJson = json, ExpiresAt = expireAt });
            }
            else
            {
                // Insert new record
                var record = new IdempotencyData
                {
                    Key = key,
                    ResultJson = json,
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = expireAt
                };
                _ = await _repository.InsertAsync(record);
            }

            // Cache
            if (result != null)
            {
                _ = _memoryCache.Set(CACHE_PREFIX + key, result, CACHE_DURATION);
            }
        }

        public string GenerateETagFromContent<T>(T content)
        {
            if (content == null)
            {
                return $"\"{Guid.NewGuid():N}\""; // Fallback to random ETag if content is null
            }

            try
            {
                // Serialize the content using the existing JSON options
                string json = JsonSerializer.Serialize(content, _jsonOptions);

                // Compute hash of the serialized content
                using var sha256 = System.Security.Cryptography.SHA256.Create();
                byte[] contentBytes = System.Text.Encoding.UTF8.GetBytes(json);
                byte[] hashBytes = sha256.ComputeHash(contentBytes);

                // Convert hash to hexadecimal string and format as ETag
                // ETags should be enclosed in quotes as per HTTP spec
                string hashString = Convert.ToHexString(hashBytes).ToLowerInvariant();
                return $"\"{hashString}\"";
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to generate content-based ETag: {ErrorMessage}", ex.Message);
                // Fallback to random ETag on error
                return $"\"{Guid.NewGuid():N}\"";
            }
        }

        public async Task<bool> HasKeyAsync(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentNullException(nameof(key));
            }

            string cacheKey = CACHE_PREFIX + "exists:" + key;
            if (_memoryCache.TryGetValue(cacheKey, out bool exists))
            {
                return exists;
            }

            var filter = Builders<IdempotencyData>.Filter.Eq(d => d.Key, key);
            var record = await _repository.GetOneAsync(filter);
            exists = record != null;
            _ = _memoryCache.Set(cacheKey, exists, CACHE_DURATION);
            return exists;
        }

        public async Task<T> ExecuteIdempotentOperationAsync<T>(string key, Func<Task<T>> operation, TimeSpan? expiration = null)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentNullException(nameof(key));
            }

            using var activity = new Activity("IdempotentOperation").SetTag("key", key).Start();
            var (found, cached) = await GetResultAsync<T>(key);
            if (found)
            {
                _ = activity.SetTag("cached", true);
                return cached;
            }

            _ = activity.SetTag("cached", false);
            try
            {
                var result = await operation();
                await StoreResultAsync(key, result, expiration);
                return result;
            }
            catch (Exception)
            {
                Logger.LogError("Operation failed for idempotency key {Key}", key);
                _ = activity.SetTag("error", true);
                throw;
            }
        }

        public async Task<bool> RemoveKeyAsync(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentNullException(nameof(key));
            }

            var filter = Builders<IdempotencyData>.Filter.Eq(d => d.Key, key);
            var existing = await _repository.GetOneAsync(filter);
            if (existing == null)
            {
                return false;
            }

            _ = await _repository.DeleteAsync(existing.Id);
            _memoryCache.Remove(CACHE_PREFIX + key);
            _memoryCache.Remove(CACHE_PREFIX + "exists:" + key);
            return true;
        }

        public async Task<ResultWrapper<long>> PurgeExpiredRecordsAsync()
        {
            var now = DateTime.UtcNow;
            var filter = Builders<IdempotencyData>.Filter.Lt(d => d.ExpiresAt, now);
            var deleted = await DeleteManyAsync(filter);
            if (deleted == null || !deleted.IsSuccess)
            {
                return ResultWrapper<long>.Failure(deleted.Reason, deleted.ErrorMessage);
            }

            Logger.LogInformation("Purged {Count} expired idempotency records", deleted);
            return ResultWrapper<long>.Success(deleted.Data.ModifiedCount);
        }
    }
}
