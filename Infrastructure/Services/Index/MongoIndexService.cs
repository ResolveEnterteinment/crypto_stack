using Application.Interfaces.Base;
using Application.Interfaces.Exchange;
using Domain.Constants.Logging;
using Domain.DTOs.Logging;
using Domain.DTOs.Settings;
using Domain.Models;              // ← for BaseEntity
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using System.Drawing;

namespace Infrastructure.Services.Index
{
    public class MongoIndexService<T> : IMongoIndexService<T>
        where T : BaseEntity    // ← must match the interface constraint
    {
        private readonly IMongoCollection<T> _collection;
        private readonly IResilienceService<T> _resilienceService;

        public MongoIndexService(
            IResilienceService<T> resilienceService,
            IMongoClient mongoClient,
            IOptions<MongoDbSettings> settings)
        {
            var db = mongoClient.GetDatabase(settings.Value.DatabaseName);
            _collection = db.GetCollection<T>(typeof(T).Name);
            _resilienceService = resilienceService ?? throw new ArgumentNullException(nameof(resilienceService));
        }

        public async Task EnsureIndexesAsync(IEnumerable<CreateIndexModel<T>> indexModels)
            => await _resilienceService.CreateBuilder(
            new Scope
            {
                NameSpace = "Infrastructure.Services.Index",
                FileName = "MongoIndexService",
                OperationName = "EnsureIndexesAsync(IEnumerable<CreateIndexModel<T>> indexModels)",
                State = new()
                {
                    ["IndexModels"] = indexModels,

                },
                LogLevel = LogLevel.Warning,
            },
            async () => _collection.Indexes.CreateManyAsync(indexModels))
            .WithMongoDbWriteResilience()
            .ExecuteAsync();
    }
}