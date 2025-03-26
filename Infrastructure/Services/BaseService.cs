using Domain.DTOs;
using Domain.Exceptions;
using Domain.Interfaces;
using Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using System.Reflection;

namespace Infrastructure.Services
{
    public abstract class BaseService<T> : IRepository<T> where T : class
    {
        private string _collectionName;

        protected readonly IMongoClient _mongoClient;
        protected readonly IMongoCollection<T> _collection;
        protected readonly ILogger _logger;

        private static readonly IReadOnlySet<string> ValidPropertyNames = GetValidPropertyNames();
        public IMongoCollection<T> Collection { get => _collection; }
        public string CollectionName { get => _collectionName; }

        protected BaseService(
            IMongoClient mongoClient,
            IOptions<MongoDbSettings> mongoDbSettings,
            string collectionName,
            ILogger logger,
            IndexKeysDefinition<T>? indexKeysDefinition = null
            )
        {
            _mongoClient = mongoClient ?? throw new ArgumentNullException(nameof(mongoClient));
            var database = mongoClient.GetDatabase(mongoDbSettings.Value.DatabaseName);
            _collectionName = collectionName;
            _collection = database.GetCollection<T>(collectionName);
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (indexKeysDefinition != null)
            {
                InitializeIndexes(indexKeysDefinition).GetAwaiter().GetResult();
            }
        }

        private async Task InitializeIndexes(IndexKeysDefinition<T> indexKeysDefinition)
        {
            try
            {
                await _collection.Indexes.CreateOneAsync(new CreateIndexModel<T>(indexKeysDefinition));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create index for collection {CollectionName}", _collectionName);
                throw new DatabaseException($"Failed to create index for collection {_collectionName}", ex);
            }
        }

        public async Task<T> GetByIdAsync(Guid id)
        {
            try
            {
                var filter = Builders<T>.Filter.Eq("Id", id);
                var result = await _collection.Find(filter).FirstOrDefaultAsync();

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

        public async Task<List<T>> GetAllAsync(FilterDefinition<T>? filter = null)
        {
            try
            {
                filter ??= Builders<T>.Filter.Empty;
                return await _collection.Find(filter).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get entities of type {EntityType}", typeof(T).Name);
                throw new DatabaseException($"Failed to retrieve {typeof(T).Name} entities", ex);
            }
        }

        public async Task<T> GetOneAsync(FilterDefinition<T> filter)
        {
            try
            {
                return await _collection.Find(filter).FirstOrDefaultAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get entity of type {EntityType} with filter", typeof(T).Name);
                throw new DatabaseException($"Failed to retrieve {typeof(T).Name} entity", ex);
            }
        }

        public async Task<InsertResult> InsertOneAsync(T entity, IClientSessionHandle? session = null)
        {
            try
            {
                ArgumentNullException.ThrowIfNull(entity, nameof(entity));

                // If entity doesn't have an Id, MongoDB will generate one
                if (session == null)
                {
                    await _collection.InsertOneAsync(entity).ConfigureAwait(false);
                }
                else
                {
                    await _collection.InsertOneAsync(session, entity).ConfigureAwait(false);
                }

                var baseEntity = entity as BaseEntity;
                if (baseEntity == null)
                {
                    _logger.LogError("Entity of type {Type} does not inherit from BaseEntity", typeof(T).Name);
                    throw new ArgumentException($"Entity of type {typeof(T).Name} does not inherit from BaseEntity");
                }

                var insertedId = baseEntity.Id; // Guid from the entity
                var insertResult = new InsertResult
                {
                    IsAcknowledged = true,
                    InsertedId = insertedId // Store as Guid in InsertResult
                };

                if (insertedId == Guid.Empty)
                {
                    throw new DatabaseException($"Inserted entity of type {typeof(T).Name} has no valid Id");
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

        public async Task<UpdateResult> UpdateOneAsync(Guid id, object updatedFields, IClientSessionHandle? session = null)
        {
            try
            {
                if (updatedFields == null)
                {
                    throw new ArgumentNullException(nameof(updatedFields), "Updated fields cannot be null.");
                }

                var updateDefinition = BuildUpdateDefinition(updatedFields);
                var filter = Builders<T>.Filter.Eq("Id", id);

                if (session == null)
                {
                    var result = await _collection.UpdateOneAsync(filter, updateDefinition).ConfigureAwait(false);

                    if (result.MatchedCount == 0)
                    {
                        _logger.LogWarning("No entity of type {EntityType} with ID {Id} found to update", typeof(T).Name, id);
                        throw new ResourceNotFoundException(typeof(T).Name, id.ToString());
                    }

                    return result;
                }
                else
                {
                    var result = await _collection.UpdateOneAsync(session, filter, updateDefinition).ConfigureAwait(false);

                    if (result.MatchedCount == 0)
                    {
                        _logger.LogWarning("No entity of type {EntityType} with ID {Id} found to update", typeof(T).Name, id);
                        throw new ResourceNotFoundException(typeof(T).Name, id.ToString());
                    }

                    return result;
                }
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

        public async Task<DeleteResult> DeleteAsync(Guid id, IClientSessionHandle? session = null)
        {
            try
            {
                var filter = Builders<T>.Filter.Eq("Id", id);
                DeleteResult result;

                if (session == null)
                {
                    result = await _collection.DeleteOneAsync(filter).ConfigureAwait(false);
                }
                else
                {
                    result = await _collection.DeleteOneAsync(session, filter).ConfigureAwait(false);
                }

                if (result.DeletedCount == 0)
                {
                    _logger.LogWarning("No entity of type {EntityType} with ID {Id} found to delete", typeof(T).Name, id);
                    throw new ResourceNotFoundException(typeof(T).Name, id.ToString());
                }

                return result;
            }
            catch (Exception ex) when (!(ex is ResourceNotFoundException))
            {
                _logger.LogError(ex, "Failed to delete entity of type {Type} with ID {Id}", typeof(T).Name, id);
                throw new DatabaseException($"Failed to delete {typeof(T).Name} with ID {id}", ex);
            }
        }

        protected virtual UpdateDefinition<T> BuildUpdateDefinition(object updatedFields)
        {
            try
            {
                var updateBuilder = Builders<T>.Update;
                var validUpdates = new List<UpdateDefinition<T>>();

                if (updatedFields.GetType() == typeof(Dictionary<string, object>))
                {
                    foreach (var field in (updatedFields as Dictionary<string, object>))
                    {
                        if (ValidPropertyNames.Contains(field.Key))
                        {
                            validUpdates.Add(updateBuilder.Set(field.Key, field.Value));
                        }
                        else
                        {
                            _logger.LogWarning("Attempted to update invalid property {PropertyName} for type {EntityType}", field.Key, typeof(T).Name);
                        }
                    }
                }
                else
                {
                    var properties = updatedFields.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
                    foreach (var property in properties)
                    {
                        var propertyName = property.Name;
                        if (ValidPropertyNames.Contains(propertyName))
                        {
                            var value = property.GetValue(updatedFields);
                            if (value != null) // Only include non-null properties in update
                            {
                                validUpdates.Add(updateBuilder.Set(propertyName, value));
                            }
                        }
                        else
                        {
                            _logger.LogWarning("Attempted to update invalid property {PropertyName} for type {EntityType}", propertyName, typeof(T).Name);
                        }
                    }
                }

                if (validUpdates.Count == 0)
                {
                    throw new ArgumentException("No valid fields provided for update.", nameof(updatedFields));
                }

                // Always update the LastUpdated field if it exists
                if (ValidPropertyNames.Contains("LastUpdated"))
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

        private static IReadOnlySet<string> GetValidPropertyNames()
        {
            return new HashSet<string>(
                typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Select(p => p.Name),
                StringComparer.OrdinalIgnoreCase
            );
        }

        /// <summary>
        /// Executes a function within a transaction
        /// </summary>
        protected async Task<TResult> ExecuteInTransactionAsync<TResult>(Func<IClientSessionHandle, Task<TResult>> action)
        {
            using var session = await _mongoClient.StartSessionAsync();
            session.StartTransaction();

            try
            {
                var result = await action(session);
                await session.CommitTransactionAsync();
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Transaction failed for {EntityType}", typeof(T).Name);
                await session.AbortTransactionAsync();
                throw;
            }
        }
    }
}