using Domain.DTOs;
using MongoDB.Driver;

public interface IRepository<T> where T : class
{
    string CollectionName { get; }
    IMongoCollection<T> Collection { get; }

    Task<T> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<T> GetOneAsync(FilterDefinition<T> filter, CancellationToken cancellationToken = default);
    Task<List<T>> GetAllAsync(FilterDefinition<T>? filter = null, CancellationToken cancellationToken = default);
    Task<InsertResult> InsertOneAsync(T entity, IClientSessionHandle? session = null, CancellationToken cancellationToken = default);
    Task<UpdateResult> UpdateOneAsync(Guid id, object updatedFields, IClientSessionHandle? session = null, CancellationToken cancellationToken = default);
    Task<DeleteResult> DeleteAsync(Guid id, IClientSessionHandle? session = null, CancellationToken cancellationToken = default);

    // 🧠 Caching methods
    /*Task<T?> GetByIdCachedAsync(Guid id, TimeSpan? expiration = null);
    Task<List<T>> GetAllCachedAsync(TimeSpan? expiration = null);
    void ResetCacheForId(Guid id);
    void ResetCacheForAll();
    void ResetCache(string key);*/
}
