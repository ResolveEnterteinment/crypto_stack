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
    /// Base service implementation for MongoDB repositories.
    /// Provides common CRUD operations and transaction support.
    /// </summary>
    /// <typeparam name="T">The entity type this repository manages</typeparam>
    public abstract class BaseService<T> : IRepository<T> where T : class
    {
        private readonly string _collectionName;
        private static readonly IReadOnlySet<string> _validPropertyNames;
        private readonly IEnumerable<CreateIndexModel<T>> _indexModels;

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
        /// <param name="mongoClient">The MongoDB client.</param>
        /// <param name="mongoDbSettings">MongoDB settings.</param>
        /// <param name="collectionName">Name of the MongoDB collection.</param>
        /// <param name="logger">Logger for diagnostic information.</param>
        /// <param name="indexModels">Optional index definition to create.</param>
        protected BaseService(
            IMongoClient mongoClient,
            IOptions<MongoDbSettings> mongoDbSettings,
            string collectionName,
            ILogger logger,
            IMemoryCache cache,
            IEnumerable<CreateIndexModel<T>>? indexModels = null
        )
        {
            _mongoClient = mongoClient ?? throw new ArgumentNullException(nameof(mongoClient));
            var database = mongoClient.GetDatabase(mongoDbSettings?.Value?.DatabaseName ??
                throw new ArgumentException("Database name is missing in settings", nameof(mongoDbSettings)));
            _collectionName = collectionName ?? throw new ArgumentNullException(nameof(collectionName));
            _collection = database.GetCollection<T>(collectionName);
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));

            if (indexModels != null && indexModels.Any())
            {
                _indexModels = indexModels;
                Task.Run(() => InitializeIndexes());
            }
        }

        /// <summary>
        /// Initializes indexes for the collection.
        /// </summary>
        /// <returns>A task that represents the asynchronous operation.</returns>
        private async Task InitializeIndexes()
        {
            try
            {
                await _collection.Indexes.CreateManyAsync(_indexModels);
                _logger.LogInformation("Successfully created indexes for collection {CollectionName}", _collectionName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create index for collection {CollectionName}", _collectionName);
                throw new DatabaseException($"Failed to create index for collection {_collectionName}", ex);
            }
        }


        /// <summary>
        /// Gets an entity by its ID.
        /// </summary>
        /// <param name="id">The entity ID.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The entity if found; otherwise, null.</returns>
        public virtual async Task<T> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            using var activity = new Activity("BaseService.GetByIdAsync").Start();
            activity?.SetTag("entity_type", typeof(T).Name);
            activity?.SetTag("entity_id", id);

            try
            {
                var filter = Builders<T>.Filter.Eq("Id", id);
                var result = await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);

                if (result == null)
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
        /// Gets all entities matching the specified filter.
        /// </summary>
        /// <param name="filter">The filter to apply.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A list of matching entities.</returns>
        public virtual async Task<List<T>> GetAllAsync(FilterDefinition<T>? filter = null, CancellationToken cancellationToken = default)
        {
            using var activity = new Activity("BaseService.GetAllAsync").Start();
            activity?.SetTag("entity_type", typeof(T).Name);

            try
            {
                filter ??= Builders<T>.Filter.Empty;
                return await _collection.Find(filter).ToListAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get entities of type {EntityType}", typeof(T).Name);
                throw new DatabaseException($"Failed to retrieve {typeof(T).Name} entities", ex);
            }
        }

        /// <summary>
        /// Gets a single entity matching the specified filter.
        /// </summary>
        /// <param name="filter">The filter to apply.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The matching entity if found; otherwise, null.</returns>
        public virtual async Task<T> GetOneAsync(FilterDefinition<T> filter, CancellationToken cancellationToken = default)
        {
            using var activity = new Activity("BaseService.GetOneAsync").Start();
            activity?.SetTag("entity_type", typeof(T).Name);

            try
            {
                return await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get entity of type {EntityType} with filter", typeof(T).Name);
                throw new DatabaseException($"Failed to retrieve {typeof(T).Name} entity", ex);
            }
        }

        /// <summary>
        /// Inserts a new entity.
        /// </summary>
        /// <param name="entity">The entity to insert.</param>
        /// <param name="session">Optional session for transaction support.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>An <see cref="InsertResult"/> containing the operation result.</returns>
        public virtual async Task<InsertResult> InsertOneAsync(T entity, IClientSessionHandle? session = null, CancellationToken cancellationToken = default)
        {
            using var activity = new Activity("BaseService.InsertOneAsync").Start();
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
                    await _collection.InsertOneAsync(entity, cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await _collection.InsertOneAsync(session, entity, cancellationToken: cancellationToken).ConfigureAwait(false);
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
        /// Updates an entity by ID.
        /// </summary>
        /// <param name="id">The ID of the entity to update.</param>
        /// <param name="updatedFields">The fields to update.</param>
        /// <param name="session">Optional session for transaction support.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The update result.</returns>
        public virtual async Task<UpdateResult> UpdateOneAsync(Guid id, object updatedFields, IClientSessionHandle? session = null, CancellationToken cancellationToken = default)
        {
            using var activity = new Activity("BaseService.UpdateOneAsync").Start();
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
                    result = await _collection.UpdateOneAsync(filter, updateDefinition, cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    result = await _collection.UpdateOneAsync(session, filter, updateDefinition, cancellationToken: cancellationToken).ConfigureAwait(false);
                }

                if (result.MatchedCount == 0)
                {
                    _logger.LogWarning("No entity of type {EntityType} with ID {Id} found to update", typeof(T).Name, id);
                    throw new ResourceNotFoundException(typeof(T).Name, id.ToString());
                }

                _logger.LogInformation("Successfully updated entity of type {EntityType} with ID {Id}", typeof(T).Name, id);
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
        /// Deletes an entity by ID.
        /// </summary>
        /// <param name="id">The ID of the entity to delete.</param>
        /// <param name="session">Optional session for transaction support.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The delete result.</returns>
        public virtual async Task<DeleteResult> DeleteAsync(Guid id, IClientSessionHandle? session = null, CancellationToken cancellationToken = default)
        {
            using var activity = new Activity("BaseService.DeleteAsync").Start();
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
                    result = await _collection.DeleteOneAsync(filter, cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    result = await _collection.DeleteOneAsync(session, filter, cancellationToken: cancellationToken).ConfigureAwait(false);
                }

                if (result.DeletedCount == 0)
                {
                    _logger.LogWarning("No entity of type {EntityType} with ID {Id} found to delete", typeof(T).Name, id);
                    throw new ResourceNotFoundException(typeof(T).Name, id.ToString());
                }

                _logger.LogInformation("Successfully deleted entity of type {EntityType} with ID {Id}", typeof(T).Name, id);
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
        /// <param name="updatedFields">The fields to update.</param>
        /// <returns>An update definition.</returns>
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
                            _logger.LogWarning("Attempted to update invalid property {PropertyName} for type {EntityType}",
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
                            _logger.LogWarning("Attempted to update invalid property {PropertyName} for type {EntityType}",
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
        /// <returns>A set of valid property names.</returns>
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
        /// <typeparam name="TResult">The result type.</typeparam>
        /// <param name="action">The function to execute.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The result of the function.</returns>
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
        /// <param name="id">The entity ID to check.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if the entity exists; otherwise, false.</returns>
        public virtual async Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
        {
            try
            {
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
        /// <param name="filter">The filter to apply.</param>
        /// <param name="page">The page number (1-based).</param>
        /// <param name="pageSize">The page size.</param>
        /// <param name="sortField">Optional field to sort by.</param>
        /// <param name="sortAscending">Whether to sort ascending.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A paginated result set.</returns>
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
                if (page < 1) page = 1;
                if (pageSize < 1) pageSize = 20;
                if (pageSize > 100) pageSize = 100; // Limit maximum page size

                // Count total matching documents
                var totalCount = await _collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);

                // Build sort definition
                SortDefinition<T> sortDefinition = null;
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

                return new PaginatedResult<T>
                {
                    Items = items,
                    Page = page,
                    PageSize = pageSize,
                    TotalCount = totalCount,
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get paginated results for type {Type}", typeof(T).Name);
                throw new DatabaseException($"Failed to get paginated results for {typeof(T).Name}", ex);
            }
        }

        protected async Task<TCache> CacheEntityAsync<TCache>(
            string key,
            Func<Task<TCache>> factory,
            TimeSpan? expiration = null)
        {
            if (_cache.TryGetValue(key, out TCache value))
                return value;

            value = await factory();
            _cache.Set(key, value, expiration ?? TimeSpan.FromMinutes(5));
            return value;
        }

        protected async Task<T?> GetByIdCachedAsync(Guid id, TimeSpan? expiration = null)
        {
            var key = $"{typeof(T).Name}_GetById_{id}";
            return await CacheEntityAsync(key, async () => await GetByIdAsync(id), expiration);
        }

        protected async Task<List<T>> GetAllCachedAsync(TimeSpan? expiration = null)
        {
            var key = $"{typeof(T).Name}_GetAll";
            return await CacheEntityAsync(key, async () => await GetAllAsync(), expiration);
        }

        protected void ResetCacheForId(Guid id)
        {
            var key = $"{typeof(T).Name}_GetById_{id}";
            _cache.Remove(key);
        }

        protected void ResetCacheForAll()
        {
            var key = $"{typeof(T).Name}_GetAll";
            _cache.Remove(key);
        }

        protected void ResetCache(string key)
        {
            _cache.Remove(key);
        }
    }
}