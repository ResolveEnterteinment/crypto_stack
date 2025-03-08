using Domain.DTOs;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Domain.Interfaces
{
    public interface IRepository<T> where T : class
    {
        Task<T> GetByIdAsync(ObjectId id);
        Task<T> GetOneAsync(FilterDefinition<T> filter);
        Task<List<T>> GetAllAsync(FilterDefinition<T> filter = null);
        Task<InsertResult> InsertOneAsync(T entity, IClientSessionHandle? session = null);
        Task<UpdateResult> UpdateOneAsync(ObjectId id, object updatedFields, IClientSessionHandle? session = null);
        Task<DeleteResult> DeleteAsync(ObjectId id, IClientSessionHandle? session = null);
    }
}
