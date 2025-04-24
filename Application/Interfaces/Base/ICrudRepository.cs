using Domain.DTOs;
using Domain.Models;
using MongoDB.Driver;

namespace Application.Interfaces.Base
{
    public interface ICrudRepository<T> where T : BaseEntity
    {
        public IMongoClient Client { get; }
        Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<T?> GetOneAsync(FilterDefinition<T> filter, CancellationToken ct = default);
        Task<List<T>> GetAllAsync(FilterDefinition<T>? filter = null, CancellationToken cancellationToken = default);
        Task<CrudResult> InsertAsync(T entity, CancellationToken cancellationToken = default);
        Task<CrudResult> UpdateAsync(Guid id, object updatedFields, CancellationToken cancellationToken = default);
        Task<CrudResult> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
        Task<CrudResult<T>> DeleteManyAsync(FilterDefinition<T> filter, CancellationToken cancellationToken = default);
        Task<bool> CheckExistsAsync(Guid id, CancellationToken cancellationToken = default);
        Task<long> CountAsync(FilterDefinition<T> filter, CancellationToken cancellationToken = default);
        Task<PaginatedResult<T>> GetPaginatedAsync(FilterDefinition<T> filter,
            SortDefinition<T> sortDefinition,
            int page = 1,
            int pageSize = 20,
            CancellationToken cancellationToken = default);
    }
}