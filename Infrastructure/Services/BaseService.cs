using Domain.DTOs;
using Domain.DTOs.Settings;
using Domain.Exceptions;
using Domain.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using System.Diagnostics;
using System.Reflection;

namespace Infrastructure.Services
{
    /// <summary>
    /// Base service implementation for MongoDB repositories with integrated in-memory caching.
    /// Provides common CRUD operations and transaction support.
    /// </summary>
    /// <typeparam name="T">The entity type this repository manages</typeparam>
    public abstract class BaseService<T> : IRepository<T> where T : BaseEntity
    {
        private readonly string _collectionName;
        private static readonly IReadOnlySet<string> _validPropertyNames;

        // Cache management constants
        private static readonly TimeSpan DEFAULT_CACHE_DURATION = TimeSpan.FromMinutes(5);
        private const string CACHE_PREFIX = "entity:";
        private const string CACHE_COLLECTION_PREFIX = "collection:";

        protected readonly IMongoClient _mongoClient;
        protected readonly IMongoCollection<T> _collection;
        protected readonly ILogger _logger;
        protected readonly IMemoryCache _cache;

        /// <summary>
        /// Gets the MongoDB collection used by this service.
        /// </summary>
        public IMongoCollection<T> Collection => _collection;

        /// <summary>
        /// Gets the name of the MongoDB collection used by this service.
        /// </summary>
        public string CollectionName => _collectionName;

        /// <summary>
        /// Static constructor to initialize valid property names.
        /// </summary>
        static BaseService()
        {
            _validPropertyNames = GetValidPropertyNames();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BaseService{T}"/> class.
        /// </summary>
        protected BaseService(
            IMongoClient mongoClient,
            IOptions<MongoDbSettings> mongoDbSettings,
            string collectionName,
            ILogger logger,
            IMemoryCache cache,
            IEnumerable<CreateIndexModel<T>>? indexModels = null)
        {
            _mongoClient = mongoClient ?? throw new ArgumentNullException(nameof(mongoClient));
            var database = mongoClient.GetDatabase(mongoDbSettings?.Value?.DatabaseName ??
                throw new ArgumentException("Database name is missing in settings", nameof(mongoDbSettings)));
            _collectionName = collectionName ?? throw new ArgumentNullException(nameof(collectionName));
            _collection = database.GetCollection<T>(collectionName);
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));

            // Initialize indexes if provided
            if (indexModels != null && indexModels.Any())
            {
                Task.Run(() => InitializeIndexesAsync(indexModels))
                    .ContinueWith(t =>
                    {
                        if (t.IsFaulted && t.Exception != null)
                        {
                            _logger.LogError(t.Exception, "Failed to initialize indexes for collection {CollectionName}", _collectionName);
                        }
                    });
            }
        }

        /// <summary>
        /// Initializes indexes for the collection with comprehensive duplicate checking and error handling.
        /// </summary>
        private async Task InitializeIndexesAsync(IEnumerable<CreateIndexModel<T>> indexModels)
        {
            try
            {
                // Get existing indexes to check for duplicates
                var existingIndexes = await _collection.Indexes.ListAsync();
                var existingIndexNames = new HashSet<string>();

                // Build a list of existing index names
                await existingIndexes.ForEachAsync(index =>
                {
                    if (index.TryGetValue("name", out var indexName) && indexName != null)
                    {
                        existingIndexNames.Add(indexName.AsString);
                    }
                });

                // Process each index individually to better handle errors
                foreach (var indexModel in indexModels)
                {
                    try
                    {
                        string indexName = indexModel.Options?.Name ?? "unnamed_index";

                        // Skip if index already exists
                        if (!string.IsNullOrEmpty(indexName) && existingIndexNames.Contains(indexName))
                        {
                            //_logger.LogInformation("Index '{IndexName}' already exists for collection {CollectionName}, skipping creation", indexName, _collectionName);
                            continue;
                        }

                        // Check if this is a unique index
                        bool isUniqueIndex = indexModel.Options?.Unique ?? false;

                        // Create the index
                        await _collection.Indexes.CreateOneAsync(indexModel);

                        _logger.LogInformation("Successfully created index '{IndexName}' for collection {CollectionName}",
                            indexName, _collectionName);
                    }
                    catch (MongoCommandException ex) when (ex.Message.Contains("duplicate key error"))
                    {
                        // Handle duplicate key errors for unique indexes
                        _logger.LogWarning("Skipping unique index creation due to existing duplicate values in collection {CollectionName}: {ErrorMessage}",
                            _collectionName, ex.Message);

                        // Here we could potentially add code to:
                        // 1. Log the specific duplicates found
                        // 2. Optionally clean up duplicates
                        // 3. Send an alert to administrators
                    }
                    catch (MongoCommandException ex) when (ex.Message.Contains("already exists"))
                    {
                        // Handle case where the index already exists but with a different name
                        //_logger.LogInformation("Index already exists with different name for collection {CollectionName}: {ErrorMessage}", _collectionName, ex.Message);
                    }
                    catch (Exception ex)
                    {
                        // Log but continue with other indexes
                        _logger.LogError(ex, "Failed to create individual index for collection {CollectionName}", _collectionName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during index initialization for collection {CollectionName}", _collectionName);
                // We're not throwing here to avoid application startup failure
                // Just log the error and continue
            }
        }

        /// <summary>
        /// Gets an entity by its ID, with caching.
        /// </summary>
        public virtual async Task<T> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            using var activity = new Activity($"{typeof(T).Name}.GetByIdAsync").Start();
            activity?.SetTag("entity_type", typeof(T).Name);
            activity?.SetTag("entity_id", id);

            // Generate cache key
            string cacheKey = GetEntityCacheKey(id);

            // Try to get from cache first
            if (_cache.TryGetValue(cacheKey, out T cachedEntity))
            {
                _logger.LogDebug("Cache hit for {EntityType} with ID {Id}", typeof(T).Name, id);
                return cachedEntity;
            }

            try
            {
                // Not in cache, get from database
                var filter = Builders<T>.Filter.Eq(e => e.Id, id);
                var result = await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);

                if (result != null)
                {
                    // Store in cache
                    _cache.Set(cacheKey, result, DEFAULT_CACHE_DURATION);
                    _logger.LogDebug("Added {EntityType} with ID {Id} to cache", typeof(T).Name, id);
                }
                else
                {
                    _logger.LogWarning("Entity of type {EntityType} with ID {Id} not found", typeof(T).Name, id);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get entity of type {EntityType} with ID {Id}", typeof(T).Name, id);
                throw new DatabaseException($"Failed to get {typeof(T).Name} with ID {id}", ex);
            }
        }

        /// <summary>
        /// Gets all entities matching the specified filter, with caching.
        /// </summary>
        public virtual async Task<List<T>> GetAllAsync(FilterDefinition<T>? filter = null, CancellationToken cancellationToken = default)
        {
            using var activity = new Activity($"{typeof(T).Name}.GetAllAsync").Start();
            activity?.SetTag("entity_type", typeof(T).Name);

            // For null or empty filter, we can use a collection-wide cache
            bool useCollectionCache = filter == null || filter == Builders<T>.Filter.Empty;
            string cacheKey = useCollectionCache
                ? GetCollectionCacheKey()
                : GetFilterCacheKey(filter?.ToString() ?? "empty");

            // Try to get from cache
            if (_cache.TryGetValue(cacheKey, out List<T> cachedEntities))
            {
                _logger.LogDebug("Cache hit for {EntityType} collection", typeof(T).Name);
                return cachedEntities;
            }

            try
            {
                // Not in cache, get from database
                filter ??= Builders<T>.Filter.Empty;
                var results = await _collection.Find(filter).ToListAsync(cancellationToken);

                // Store in cache
                _cache.Set(cacheKey, results, DEFAULT_CACHE_DURATION);
                _logger.LogDebug("Added {EntityType} collection to cache with key {CacheKey}", typeof(T).Name, cacheKey);

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get entities of type {EntityType}", typeof(T).Name);
                throw new DatabaseException($"Failed to retrieve {typeof(T).Name} entities", ex);
            }
        }

        /// <summary>
        /// Gets a single entity matching the specified filter, with caching.
        /// </summary>
        public virtual async Task<T> GetOneAsync(FilterDefinition<T> filter, CancellationToken cancellationToken = default)
        {
            using var activity = new Activity($"{typeof(T).Name}.GetOneAsync").Start();
            activity?.SetTag("entity_type", typeof(T).Name);

            string cacheKey = GetFilterCacheKey(filter.ToString());

            // Try to get from cache
            if (_cache.TryGetValue(cacheKey, out T cachedEntity))
            {
                _logger.LogDebug("Cache hit for {EntityType} with filter", typeof(T).Name);
                return cachedEntity;
            }

            try
            {
                // Not in cache, get from database
                var result = await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);

                if (result != null)
                {
                    // Store in cache
                    _cache.Set(cacheKey, result, DEFAULT_CACHE_DURATION);
                    _logger.LogDebug("Added {EntityType} with filter to cache", typeof(T).Name);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get entity of type {EntityType} with filter", typeof(T).Name);
                throw new DatabaseException($"Failed to retrieve {typeof(T).Name} entity", ex);
            }
        }

        /// <summary>
        /// Inserts a new entity and invalidates relevant caches.
        /// </summary>
        public virtual async Task<InsertResult> InsertOneAsync(T entity, IClientSessionHandle? session = null, CancellationToken cancellationToken = default)
        {
            using var activity = new Activity($"{typeof(T).Name}.InsertOneAsync").Start();
            activity?.SetTag("entity_type", typeof(T).Name);

            try
            {
                ArgumentNullException.ThrowIfNull(entity, nameof(entity));

                // Ensure ID is set for BaseEntity types
                if (entity is BaseEntity baseEntity && baseEntity.Id == Guid.Empty)
                {
                    baseEntity.Id = Guid.NewGuid();
                }

                // Set creation time if not already set
                if (entity is BaseEntity baseEntityWithTime && baseEntityWithTime.CreatedAt == default)
                {
                    baseEntityWithTime.CreatedAt = DateTime.UtcNow;
                }

                // Insert using session if provided
                if (session == null)
                {
                    await _collection.InsertOneAsync(entity, cancellationToken: cancellationToken);
                }
                else
                {
                    await _collection.InsertOneAsync(session, entity, cancellationToken: cancellationToken);
                }

                // Extract ID from the entity
                Guid? insertedId = null;
                if (entity is BaseEntity baseEntityId)
                {
                    insertedId = baseEntityId.Id;

                    if (insertedId == Guid.Empty)
                    {
                        throw new DatabaseException($"Inserted entity of type {typeof(T).Name} has no valid Id");
                    }
                }

                var insertResult = new InsertResult
                {
                    IsAcknowledged = true,
                    InsertedId = insertedId
                };

                _logger.LogInformation("Successfully inserted entity of type {EntityType} with ID {EntityId}",
                    typeof(T).Name, insertedId);

                // Invalidate collection cache
                InvalidateCollectionCache();

                // If we know the ID, also invalidate entity cache
                if (insertedId.HasValue)
                {
                    InvalidateEntityCache(insertedId.Value);
                }

                return insertResult;
            }
            catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                _logger.LogWarning(ex, "Duplicate key violation when inserting entity of type {Type}", typeof(T).Name);
                throw new ConcurrencyException(typeof(T).Name, "unknown");
            }
            catch (Exception ex) when (!(ex is ArgumentNullException || ex is DatabaseException || ex is ConcurrencyException))
            {
                _logger.LogError(ex, "Failed to insert entity of type {Type}", typeof(T).Name);
                throw new DatabaseException($"Failed to insert {typeof(T).Name}", ex);
            }
        }

        /// <summary>
        /// Updates an entity by ID and invalidates relevant caches.
        /// </summary>
        public virtual async Task<UpdateResult> UpdateOneAsync(Guid id, object updatedFields, IClientSessionHandle? session = null, CancellationToken cancellationToken = default)
        {
            using var activity = new Activity($"{typeof(T).Name}.UpdateOneAsync").Start();
            activity?.SetTag("entity_type", typeof(T).Name);
            activity?.SetTag("entity_id", id);

            try
            {
                if (updatedFields == null)
                {
                    throw new ArgumentNullException(nameof(updatedFields), "Updated fields cannot be null.");
                }

                if (id == Guid.Empty)
                {
                    throw new ArgumentException("ID cannot be empty", nameof(id));
                }

                var updateDefinition = BuildUpdateDefinition(updatedFields);
                var filter = Builders<T>.Filter.Eq("Id", id);

                UpdateResult result;
                if (session == null)
                {
                    result = await _collection.UpdateOneAsync(filter, updateDefinition, cancellationToken: cancellationToken);
                }
                else
                {
                    result = await _collection.UpdateOneAsync(session, filter, updateDefinition, cancellationToken: cancellationToken);
                }

                if (result.MatchedCount == 0)
                {
                    _logger.LogWarning("No entity of type {EntityType} with ID {Id} found to update", typeof(T).Name, id);
                    throw new ResourceNotFoundException(typeof(T).Name, id.ToString());
                }

                _logger.LogInformation("Successfully updated entity of type {EntityType} with ID {Id}", typeof(T).Name, id);

                // Invalidate caches
                InvalidateEntityCache(id);
                InvalidateCollectionCache();

                return result;
            }
            catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                _logger.LogWarning(ex, "Duplicate key violation when updating entity of type {Type} with ID {Id}", typeof(T).Name, id);
                throw new ConcurrencyException(typeof(T).Name, id.ToString());
            }
            catch (Exception ex) when (!(ex is ArgumentNullException || ex is ResourceNotFoundException || ex is ConcurrencyException))
            {
                _logger.LogError(ex, "Failed to update entity of type {Type} with ID {Id}", typeof(T).Name, id);
                throw new DatabaseException($"Failed to update {typeof(T).Name} with ID {id}", ex);
            }
        }

        /// <summary>
        /// Deletes an entity by ID and invalidates relevant caches.
        /// </summary>
        public virtual async Task<DeleteResult> DeleteAsync(Guid id, IClientSessionHandle? session = null, CancellationToken cancellationToken = default)
        {
            using var activity = new Activity($"{typeof(T).Name}.DeleteAsync").Start();
            activity?.SetTag("entity_type", typeof(T).Name);
            activity?.SetTag("entity_id", id);

            try
            {
                if (id == Guid.Empty)
                {
                    throw new ArgumentException("ID cannot be empty", nameof(id));
                }

                var filter = Builders<T>.Filter.Eq("Id", id);
                DeleteResult result;

                if (session == null)
                {
                    result = await _collection.DeleteOneAsync(filter, cancellationToken: cancellationToken);
                }
                else
                {
                    result = await _collection.DeleteOneAsync(session, filter, cancellationToken: cancellationToken);
                }

                if (result.DeletedCount == 0)
                {
                    _logger.LogWarning("No entity of type {EntityType} with ID {Id} found to delete", typeof(T).Name, id);
                    throw new ResourceNotFoundException(typeof(T).Name, id.ToString());
                }

                _logger.LogInformation("Successfully deleted entity of type {EntityType} with ID {Id}", typeof(T).Name, id);

                // Invalidate caches
                InvalidateEntityCache(id);
                InvalidateCollectionCache();

                return result;
            }
            catch (Exception ex) when (!(ex is ArgumentException || ex is ResourceNotFoundException))
            {
                _logger.LogError(ex, "Failed to delete entity of type {Type} with ID {Id}", typeof(T).Name, id);
                throw new DatabaseException($"Failed to delete {typeof(T).Name} with ID {id}", ex);
            }
        }

        /// <summary>
        /// Builds an update definition from the provided field values.
        /// </summary>
        protected virtual UpdateDefinition<T> BuildUpdateDefinition(object updatedFields)
        {
            try
            {
                var updateBuilder = Builders<T>.Update;
                var validUpdates = new List<UpdateDefinition<T>>();

                if (updatedFields is Dictionary<string, object> fieldDict)
                {
                    foreach (var field in fieldDict)
                    {
                        if (_validPropertyNames.Contains(field.Key))
                        {
                            validUpdates.Add(updateBuilder.Set(field.Key, field.Value));
                        }
                        else
                        {
                            _logger.LogWarning("Skipping invalid property {PropertyName} for type {EntityType}",
                                field.Key, typeof(T).Name);
                        }
                    }
                }
                else
                {
                    var properties = updatedFields.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
                    foreach (var property in properties)
                    {
                        var propertyName = property.Name;
                        if (_validPropertyNames.Contains(propertyName))
                        {
                            var value = property.GetValue(updatedFields);
                            if (value != null) // Only include non-null properties in update
                            {
                                validUpdates.Add(updateBuilder.Set(propertyName, value));
                            }
                        }
                        else
                        {
                            _logger.LogWarning("Skipping invalid property {PropertyName} for type {EntityType}",
                                propertyName, typeof(T).Name);
                        }
                    }
                }

                if (validUpdates.Count == 0)
                {
                    throw new ArgumentException("No valid fields provided for update.", nameof(updatedFields));
                }

                // Always update the LastUpdated field if it exists
                if (_validPropertyNames.Contains("LastUpdated"))
                {
                    validUpdates.Add(updateBuilder.Set("LastUpdated", DateTime.UtcNow));
                }

                return updateBuilder.Combine(validUpdates);
            }
            catch (Exception ex) when (!(ex is ArgumentException))
            {
                _logger.LogError(ex, "Failed to build update definition for type {EntityType}", typeof(T).Name);
                throw new DatabaseException($"Failed to prepare update for {typeof(T).Name}", ex);
            }
        }

        /// <summary>
        /// Gets the names of all valid properties for the entity type.
        /// </summary>
        private static IReadOnlySet<string> GetValidPropertyNames()
        {
            return new HashSet<string>(
                typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Select(p => p.Name),
                StringComparer.OrdinalIgnoreCase
            );
        }

        /// <summary>
        /// Executes a function within a MongoDB transaction.
        /// </summary>
        protected async Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<IClientSessionHandle, Task<TResult>> action,
            CancellationToken cancellationToken = default)
        {
            using var session = await _mongoClient.StartSessionAsync(cancellationToken: cancellationToken);
            session.StartTransaction();

            try
            {
                var result = await action(session);
                await session.CommitTransactionAsync(cancellationToken);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Transaction failed for {EntityType}", typeof(T).Name);

                try
                {
                    await session.AbortTransactionAsync(cancellationToken);
                }
                catch (Exception abortEx)
                {
                    _logger.LogError(abortEx, "Failed to abort transaction after error");
                }

                throw;
            }
        }

        /// <summary>
        /// Checks if an entity with the specified ID exists.
        /// </summary>
        public virtual async Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
        {
            try
            {
                // Check cache first
                string cacheKey = GetEntityCacheKey(id);
                if (_cache.TryGetValue(cacheKey, out T _))
                {
                    return true;
                }

                // Not in cache, check database
                var filter = Builders<T>.Filter.Eq("Id", id);
                return await _collection.CountDocumentsAsync(filter, new CountOptions { Limit = 1 }, cancellationToken) > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check existence of entity of type {Type} with ID {Id}", typeof(T).Name, id);
                throw new DatabaseException($"Failed to check existence of {typeof(T).Name} with ID {id}", ex);
            }
        }

        /// <summary>
        /// Creates a paginated result set.
        /// </summary>
        public virtual async Task<PaginatedResult<T>> GetPaginatedAsync(
            FilterDefinition<T> filter,
            int page = 1,
            int pageSize = 20,
            string sortField = null,
            bool sortAscending = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Normalize pagination parameters
                page = Math.Max(1, page);
                pageSize = Math.Clamp(pageSize, 1, 100);

                // Generate cache key for this paginated request
                string cacheKey = $"paginated:{typeof(T).Name}:{filter}:{page}:{pageSize}:{sortField}:{sortAscending}";

                // Try to get from cache
                if (_cache.TryGetValue(cacheKey, out PaginatedResult<T> cachedResult))
                {
                    return cachedResult;
                }

                // Count total matching documents
                var totalCount = await _collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);

                // Build sort definition
                SortDefinition<T> sortDefinition;
                if (!string.IsNullOrEmpty(sortField) && _validPropertyNames.Contains(sortField))
                {
                    sortDefinition = sortAscending
                        ? Builders<T>.Sort.Ascending(sortField)
                        : Builders<T>.Sort.Descending(sortField);
                }
                else
                {
                    // Default sort by ID
                    sortDefinition = Builders<T>.Sort.Descending("Id");
                }

                // Get paginated results
                var skip = (page - 1) * pageSize;
                var items = await _collection.Find(filter)
                    .Sort(sortDefinition)
                    .Skip(skip)
                    .Limit(pageSize)
                    .ToListAsync(cancellationToken);

                var result = new PaginatedResult<T>
                {
                    Items = items,
                    Page = page,
                    PageSize = pageSize,
                    TotalCount = totalCount,
                };

                // Store in cache
                _cache.Set(cacheKey, result, TimeSpan.FromMinutes(1)); // Short cache duration for paginated results

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get paginated results for type {Type}", typeof(T).Name);
                throw new DatabaseException($"Failed to get paginated results for {typeof(T).Name}", ex);
            }
        }

        #region Cache Helpers

        /// <summary>
        /// Gets the cache key for an entity by ID.
        /// </summary>
        protected string GetEntityCacheKey(Guid id)
        {
            return $"{CACHE_PREFIX}{typeof(T).Name}:{id}";
        }

        /// <summary>
        /// Gets the cache key for the entire collection.
        /// </summary>
        protected string GetCollectionCacheKey()
        {
            return $"{CACHE_COLLECTION_PREFIX}{typeof(T).Name}";
        }

        /// <summary>
        /// Gets the cache key for a filtered result.
        /// </summary>
        protected string GetFilterCacheKey(string filterString)
        {
            // Create deterministic hash of filter string to avoid excessively long keys
            int filterHash = filterString.GetHashCode();
            return $"{CACHE_COLLECTION_PREFIX}{typeof(T).Name}:filter:{filterHash}";
        }

        /// <summary>
        /// Invalidates the cache for a specific entity.
        /// </summary>
        protected void InvalidateEntityCache(Guid id)
        {
            string cacheKey = GetEntityCacheKey(id);
            _cache.Remove(cacheKey);
            _logger.LogDebug("Invalidated cache for {EntityType} with ID {Id}", typeof(T).Name, id);
        }

        /// <summary>
        /// Invalidates the cache for the entire collection.
        /// </summary>
        protected void InvalidateCollectionCache()
        {
            string collectionCacheKey = GetCollectionCacheKey();
            _cache.Remove(collectionCacheKey);
            _logger.LogDebug("Invalidated collection cache for {EntityType}", typeof(T).Name);

            // Also clear any filtered result caches by creating a dummy entry with pattern prefix
            // Since there's no direct way to remove by pattern in IMemoryCache
            _cache.GetOrCreate($"{CACHE_COLLECTION_PREFIX}{typeof(T).Name}:INVALIDATE_TIMESTAMP", entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(1);
                return DateTime.UtcNow.Ticks;
            });
        }

        /// <summary>
        /// Generic method to get or create a cached entity.
        /// </summary>
        protected async Task<T> GetOrCreateCachedEntityAsync(
            Guid id,
            Func<Task<T>> factory,
            TimeSpan? expiration = null)
        {
            string cacheKey = GetEntityCacheKey(id);
            return await GetOrCreateCachedItemAsync(cacheKey, factory, expiration);
        }

        /// <summary>
        /// Generic method to get or create a cached collection.
        /// </summary>
        protected async Task<List<T>> GetOrCreateCachedCollectionAsync(
            Func<Task<List<T>>> factory,
            TimeSpan? expiration = null)
        {
            string cacheKey = GetCollectionCacheKey();
            return await GetOrCreateCachedItemAsync(cacheKey, factory, expiration);
        }

        /// <summary>
        /// Generic method to get or create a cached item.
        /// </summary>
        protected async Task<TItem> GetOrCreateCachedItemAsync<TItem>(
            string cacheKey,
            Func<Task<TItem>> factory,
            TimeSpan? expiration = null)
        {
            if (_cache.TryGetValue(cacheKey, out TItem cachedItem))
            {
                return cachedItem;
            }

            var item = await factory();

            if (item != null)
            {
                _cache.Set(cacheKey, item, expiration ?? DEFAULT_CACHE_DURATION);
                _logger.LogDebug("Added item to cache with key {CacheKey}", cacheKey);
            }

            return item;
        }

        #endregion
    }
}