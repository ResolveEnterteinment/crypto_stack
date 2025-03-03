using Domain.DTOs;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Domain.Interfaces
{
    public interface IRepository<T> where T : class
    {
        Task<T> GetByIdAsync(ObjectId id);
        Task<List<T>> GetAllAsync(FilterDefinition<T> filter = null);
        Task<InsertResult> InsertOneAsync(T entity);
        Task<InsertResult> InsertOneAsync(IClientSessionHandle session, T entity);
        Task<UpdateResult> UpdateOneAsync(ObjectId id, object updatedFields);
        Task<UpdateResult> UpdateOneAsync(IClientSessionHandle session, ObjectId id, object updatedFields);
        Task<DeleteResult> DeleteAsync(ObjectId id);
        Task<DeleteResult> DeleteAsync(IClientSessionHandle session, ObjectId id);
    }
}
