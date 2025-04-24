using Application.Interfaces.Base;
using Domain.Attributes;
using Domain.DTOs;
using Domain.DTOs.Settings;
using Domain.Exceptions;
using Domain.Models;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using System.Reflection;

namespace Infrastructure.Services.Base
{
    public class Repository<T> : ICrudRepository<T> where T : BaseEntity
    {
        private readonly IMongoClient _client;
        private readonly IMongoDatabase _database;
        protected readonly IMongoCollection<T> Collection;

        public IMongoClient Client => _client;

        public Repository(IMongoClient client, IOptions<MongoDbSettings> mongoSettings)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            var settings = mongoSettings?.Value
                ?? throw new ArgumentNullException(nameof(mongoSettings));

            _database = _client.GetDatabase(settings.DatabaseName);

            var bsonColl = typeof(T).GetCustomAttribute<BsonCollectionAttribute>();

            var collectionName = bsonColl?.CollectionName
                ?? typeof(T).Name.Replace("Data", string.Empty).ToLowerInvariant() + "s";

            Collection = _database.GetCollection<T>(collectionName);
        }

        public Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var filter = Builders<T>.Filter.Eq(e => e.Id, id);
            return Collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
        }

        public Task<T?> GetOneAsync(FilterDefinition<T> filter, CancellationToken ct = default)
            => Collection.Find(filter).FirstOrDefaultAsync(ct);

        public Task<List<T>> GetAllAsync(FilterDefinition<T>? filter = null, CancellationToken cancellationToken = default)
        {
            return Collection.Find(filter ?? Builders<T>.Filter.Empty).ToListAsync(cancellationToken);
        }

        public async Task<CrudResult> InsertAsync(T entity, CancellationToken cancellationToken = default)
        {
            try
            {
                await Collection.InsertOneAsync(entity, cancellationToken);

                var insertedId = entity.Id;
                if (insertedId == Guid.Empty)
                {
                    throw new DatabaseException($"Inserted entity of type {typeof(T).Name} has no valid Id");
                }

                return new CrudResult<T>
                {
                    IsSuccess = true,
                    MatchedCount = 0,
                    ModifiedCount = 1,
                    AffectedIds = new[] { insertedId },
                    Documents = new[] { entity }
                };
            }
            catch (Exception ex)
            {
                return new CrudResult<T>
                {
                    IsSuccess = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<CrudResult> UpdateAsync(Guid id, object updatedFields, CancellationToken cancellationToken = default)
        {
            try
            {
                var existing = await GetByIdAsync(id, cancellationToken)
                    ?? throw new ResourceNotFoundException(typeof(T).Name, id.ToString());

                var updateDefinition = CreateUpdateDefinition(updatedFields);
                var filter = Builders<T>.Filter.Eq(e => e.Id, id);
                var mongoResult = await Collection.UpdateOneAsync(filter, updateDefinition, cancellationToken: cancellationToken);

                return new CrudResult
                {
                    IsSuccess = mongoResult.IsAcknowledged,
                    MatchedCount = mongoResult.MatchedCount,
                    ModifiedCount = mongoResult.ModifiedCount,
                    AffectedIds = new[] { id }
                };
            }
            catch (Exception ex)
            {
                return new CrudResult
                {
                    IsSuccess = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<CrudResult> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            try
            {
                var toDelete = await GetByIdAsync(id, cancellationToken)
                    ?? throw new ResourceNotFoundException(typeof(T).Name, id.ToString());

                var filter = Builders<T>.Filter.Eq(e => e.Id, id);
                var deleteResult = await Collection.DeleteOneAsync(filter, cancellationToken: cancellationToken);

                return new CrudResult
                {
                    IsSuccess = deleteResult.IsAcknowledged,
                    MatchedCount = deleteResult.DeletedCount,
                    ModifiedCount = deleteResult.DeletedCount,
                    AffectedIds = new[] { id }
                };
            }
            catch (Exception ex)
            {
                return new CrudResult
                {
                    IsSuccess = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<CrudResult<T>> DeleteManyAsync(FilterDefinition<T> filter, CancellationToken cancellationToken = default)
        {
            try
            {
                var docs = await Collection.Find(filter).ToListAsync(cancellationToken);
                var deleteResult = await Collection.DeleteManyAsync(filter, cancellationToken);

                return new CrudResult<T>
                {
                    IsSuccess = deleteResult.IsAcknowledged,
                    MatchedCount = docs.Count,
                    ModifiedCount = deleteResult.DeletedCount,
                    AffectedIds = docs.Select(d => d.Id),
                    Documents = docs
                };
            }
            catch (Exception ex)
            {
                return new CrudResult<T>
                {
                    IsSuccess = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public virtual async Task<bool> CheckExistsAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var filter = Builders<T>.Filter.Eq(e => e.Id, id);
            return await Collection.CountDocumentsAsync(filter, new CountOptions { Limit = 1 }, cancellationToken) > 0;
        }

        public virtual async Task<long> CountAsync(FilterDefinition<T> filter, CancellationToken cancellationToken = default)
        {
            return await Collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
        }

        public virtual async Task<PaginatedResult<T>> GetPaginatedAsync(FilterDefinition<T> filter,
            SortDefinition<T> sortDefinition,
            int page = 1,
            int pageSize = 20,
            CancellationToken cancellationToken = default)
        {
            var totalCount = await CountAsync(filter, cancellationToken);

            var items = await Collection.Find(filter)
                .Sort(sortDefinition)
                .Skip((page - 1) * pageSize)
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

        private UpdateDefinition<T> CreateUpdateDefinition(object updatedFields)
        {
            var updates = Builders<T>.Update;
            var updateDefs = new List<UpdateDefinition<T>>();

            if (updatedFields is IDictionary<string, object> dict)
            {
                foreach (var kv in dict)
                {
                    updateDefs.Add(updates.Set(kv.Key, kv.Value));
                }
            }
            else
            {
                foreach (var prop in updatedFields.GetType().GetProperties())
                {
                    var value = prop.GetValue(updatedFields);
                    if (value is not null)
                    {
                        updateDefs.Add(updates.Set(prop.Name, value));
                    }
                }
            }

            if (!updateDefs.Any())
            {
                throw new ArgumentException("No valid fields provided", nameof(updatedFields));
            }

            return updates.Combine(updateDefs);
        }
    }
}
