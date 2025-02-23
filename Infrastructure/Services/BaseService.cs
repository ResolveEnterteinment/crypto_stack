using AspNetCore.Identity.MongoDbCore.Infrastructure;
using Domain.DTOs;
using Domain.Interfaces;
using Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Reflection;

namespace Infrastructure.Services
{
    public abstract class BaseService<T> : IRepository<T> where T : class
    {
        protected readonly IMongoCollection<T> _collection;
        protected readonly ILogger _logger;
        private static readonly IReadOnlySet<string> ValidPropertyNames = GetValidPropertyNames();

        protected BaseService(IMongoClient mongoClient, IOptions<MongoDbSettings> mongoDbSettings, string collectionName, ILogger logger)
        {
            var database = mongoClient.GetDatabase(mongoDbSettings.Value.DatabaseName);
            _collection = database.GetCollection<T>(collectionName);
            _logger = logger;
        }

        public async Task<T> GetByIdAsync(ObjectId id)
        {
            var filter = Builders<T>.Filter.Eq("_id", id);
            return await _collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<List<T>> GetAllAsync(FilterDefinition<T> filter = null)
        {
            filter ??= Builders<T>.Filter.Empty;
            return await _collection.Find(filter).ToListAsync();
        }

        public async Task<InsertResult> InsertAsync(T entity)
        {
            if (entity is null) return new InsertResult()
            {
                IsAcknowledged = false,
                InsertedId = null,
                ErrorMessage = $"Invalid argument. {nameof(entity)} is null."
            };
            try
            {
                await _collection.InsertOneAsync(entity).ConfigureAwait(false);
                return new InsertResult()
                {
                    IsAcknowledged = true,
                    InsertedId = (entity as BaseEntity)._id.ToString()// orderData now includes its generated ObjectId (if not already set)
                };
            }
            catch (MongoException ex)
            {
                _logger.LogError(ex, "Failed to insert entity of type {Type}", typeof(T).Name);
                return new InsertResult()
                {
                    IsAcknowledged = false,
                    InsertedId = null,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<UpdateResult> UpdateAsync(ObjectId id, object updatedFields)
        {
            if (updatedFields == null)
            {
                throw new ArgumentNullException(nameof(updatedFields), "Updated fields cannot be null.");
            }

            var updateDefinition = BuildUpdateDefinition(updatedFields);
            var filter = Builders<T>.Filter.Eq("_id", id);

            try
            {
                return await _collection.UpdateOneAsync(filter, updateDefinition).ConfigureAwait(false);
            }
            catch (MongoException ex)
            {
                _logger.LogError(ex, "Failed to update entity with ID {Id}", id);
                throw new InvalidOperationException($"Failed to update entity with ID {id}.", ex);
            }
        }

        public async Task<DeleteResult> DeleteAsync(ObjectId id)
        {
            var filter = Builders<T>.Filter.Eq("_id", id);
            try
            {
                return await _collection.DeleteOneAsync(filter).ConfigureAwait(false);
            }
            catch (MongoException ex)
            {
                _logger.LogError(ex, "Failed to delete entity with ID {Id}", id);
                throw new InvalidOperationException($"Failed to delete entity with ID {id}.", ex);
            }
        }

        protected virtual UpdateDefinition<T> BuildUpdateDefinition(object updatedFields)
        {
            var bsonDocument = updatedFields.ToBsonDocument();
            var updateBuilder = Builders<T>.Update;
            var validUpdates = new List<UpdateDefinition<T>>();

            foreach (var element in bsonDocument.Elements)
            {
                if (ValidPropertyNames.Contains(element.Name))
                {
                    validUpdates.Add(updateBuilder.Set(element.Name, element.Value));
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