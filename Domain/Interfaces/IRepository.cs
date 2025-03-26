using Domain.DTOs;
using MongoDB.Driver;

namespace Domain.Interfaces
{
    public interface IRepository<T> where T : class
    {
        public string CollectionName { get; }
        public IMongoCollection<T> Collection { get; }
        Task<T> GetByIdAsync(Guid id);
        Task<T> GetOneAsync(FilterDefinition<T> filter);
        Task<List<T>> GetAllAsync(FilterDefinition<T> filter = null);
        Task<InsertResult> InsertOneAsync(T entity, IClientSessionHandle? session = null);
        Task<UpdateResult> UpdateOneAsync(Guid id, object updatedFields, IClientSessionHandle? session = null);
        Task<DeleteResult> DeleteAsync(Guid id, IClientSessionHandle? session = null);
    }
}
