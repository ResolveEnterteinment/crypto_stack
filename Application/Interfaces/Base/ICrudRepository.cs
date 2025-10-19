using Domain.DTOs;
using Domain.Models;
using MongoDB.Driver;

namespace Application.Interfaces.Base
{
    public interface ICrudRepository<T> where T : BaseEntity
    {
        public IMongoClient Client { get; }
        public string CollectionName { get; }
        Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<T?> GetOneAsync(FilterDefinition<T> filter, CancellationToken ct = default);
        Task<T?> GetOneAsync(FilterDefinition<T> filter, SortDefinition<T> sort, CancellationToken ct = default);
        Task<List<T>> GetAllAsync(FilterDefinition<T> filter = null, CancellationToken cancellationToken = default);
        Task<List<T>> GetAllAsync(FilterDefinition<T> filter, SortDefinition<T> sort, CancellationToken cancellationToken = default);
        Task<CrudResult<T>> InsertAsync(T entity, CancellationToken cancellationToken = default);
        Task<CrudResult<T>> UpdateAsync(Guid id, object updatedFields, CancellationToken cancellationToken = default);
        Task<CrudResult<T>> UpdateManyAsync(FilterDefinition<T> filter, object updatedFields, CancellationToken cancellationToken = default);
        Task<CrudResult<T>> ReplaceAsync(FilterDefinition<T> filter, T entity, CancellationToken cancellationToken = default);
        Task<CrudResult<T>> ReplaceManyAsync(List<T> entities, CancellationToken cancellationToken = default);
        Task<CrudResult<T>> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
        Task<CrudResult<T>> DeleteManyAsync(FilterDefinition<T> filter, CancellationToken cancellationToken = default);
        Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);
        Task<long> CountAsync(FilterDefinition<T> filter, CancellationToken cancellationToken = default);
        Task<PaginatedResult<T>> GetPaginatedAsync(FilterDefinition<T> filter,
            SortDefinition<T> sortDefinition,
            int page = 1,
            int pageSize = 20,
            CancellationToken cancellationToken = default);
    }
}