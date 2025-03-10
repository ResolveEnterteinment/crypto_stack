using AspNetCore.Identity.MongoDbCore.Infrastructure;
using Domain.DTOs;
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
        protected readonly IMongoClient _mongoClient;
        protected readonly IMongoCollection<T> _collection;
        protected readonly ILogger _logger;

        private static readonly IReadOnlySet<string> ValidPropertyNames = GetValidPropertyNames();

        protected BaseService(
            IMongoClient mongoClient,
            IOptions<MongoDbSettings> mongoDbSettings,
            string collectionName,
            ILogger logger)
        {
            _mongoClient = mongoClient ?? throw new ArgumentNullException(nameof(mongoClient));
            var database = mongoClient.GetDatabase(mongoDbSettings.Value.DatabaseName);
            _collection = database.GetCollection<T>(collectionName);
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<T> GetByIdAsync(Guid id)
        {
            var filter = Builders<T>.Filter.Eq("_id", id);
            return await _collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<List<T>> GetAllAsync(FilterDefinition<T>? filter = null)
        {
            filter ??= Builders<T>.Filter.Empty;
            return await _collection.Find(filter).ToListAsync();
        }

        public async Task<T> GetOneAsync(FilterDefinition<T> filter)
        {
            return await _collection.Find(filter).FirstOrDefaultAsync();
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
                    throw new ArgumentNullException(nameof(baseEntity));
                }

                var insertedId = baseEntity.Id; // Guid from the entity
                var insertResult = new InsertResult
                {
                    IsAcknowledged = true,
                    InsertedId = insertedId // Store as string in InsertResult
                };

                if (insertedId == Guid.Empty)
                {
                    throw new MongoException($"Inserted entity of type {typeof(T).Name} has no valid Id");
                }

                return insertResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to insert entity of type {Type}", typeof(T).Name);
                return new InsertResult
                {
                    IsAcknowledged = false,
                    InsertedId = null,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<UpdateResult> UpdateOneAsync(Guid id, object updatedFields, IClientSessionHandle? session = null)
        {
            if (updatedFields == null)
            {
                throw new ArgumentNullException(nameof(updatedFields), "Updated fields cannot be null.");
            }

            var updateDefinition = BuildUpdateDefinition(updatedFields);
            var filter = Builders<T>.Filter.Eq("_id", id);

            try
            {
                if (session == null)
                {
                    return await _collection.UpdateOneAsync(filter, updateDefinition).ConfigureAwait(false);

                }
                else
                {
                    return await _collection.UpdateOneAsync(session, filter, updateDefinition).ConfigureAwait(false);
                }
            }
            catch (MongoException ex)
            {
                _logger.LogError(ex, "Failed to update entity with ID {Id}", id);
                throw new InvalidOperationException($"Failed to update entity with ID {id}.", ex);
            }
        }

        public async Task<DeleteResult> DeleteAsync(Guid id, IClientSessionHandle? session = null)
        {
            var filter = Builders<T>.Filter.Eq("_id", id);
            try
            {
                if (session == null)
                {
                    return await _collection.DeleteOneAsync(filter).ConfigureAwait(false);
                }
                else
                {
                    return await _collection.DeleteOneAsync(session, filter).ConfigureAwait(false);
                }
            }
            catch (MongoException ex)
            {
                _logger.LogError(ex, "Failed to delete entity with ID {Id}", id);
                throw new InvalidOperationException($"Failed to delete entity with ID {id}.", ex);
            }
        }

        protected virtual UpdateDefinition<T> BuildUpdateDefinition(object updatedFields)
        {
            var updateBuilder = Builders<T>.Update;
            var validUpdates = new List<UpdateDefinition<T>>();

            var properties = updatedFields.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var property in properties)
            {
                var propertyName = property.Name;
                if (ValidPropertyNames.Contains(propertyName))
                {
                    var value = property.GetValue(updatedFields);
                    validUpdates.Add(updateBuilder.Set(propertyName, value));
                }
            }

            if (validUpdates.Count == 0)
            {
                throw new ArgumentException("No valid fields provided for update.", nameof(updatedFields));
            }

            return updateBuilder.Combine(validUpdates);
        }

        private static IReadOnlySet<string> GetValidPropertyNames()
        {
            return new HashSet<string>(
                typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Select(p => p.Name),
                StringComparer.OrdinalIgnoreCase
            );
        }
    }
}