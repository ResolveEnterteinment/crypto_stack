using Application.Interfaces.Base;
using Domain.DTOs.Settings;
using Domain.Models;              // ← for BaseEntity
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Infrastructure.Services.Index
{
    public class MongoIndexService<T> : IMongoIndexService<T>
        where T : BaseEntity    // ← must match the interface constraint
    {
        private readonly IMongoCollection<T> _collection;

        public MongoIndexService(
            IMongoClient mongoClient,
            IOptions<MongoDbSettings> settings)
        {
            var db = mongoClient.GetDatabase(settings.Value.DatabaseName);
            _collection = db.GetCollection<T>(typeof(T).Name);
        }

        public Task EnsureIndexesAsync(IEnumerable<CreateIndexModel<T>> indexModels)
            => _collection.Indexes.CreateManyAsync(indexModels);
    }
}