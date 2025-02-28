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
        Task<UpdateResult> UpdateAsync(ObjectId id, object updatedFields);
        Task<DeleteResult> DeleteAsync(ObjectId id);
    }
}
