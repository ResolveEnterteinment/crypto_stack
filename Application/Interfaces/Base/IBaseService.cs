using Domain.DTOs;
using Domain.Models;
using MongoDB.Driver;

namespace Application.Interfaces.Base
{
    /// <summary>
    /// Generic base service interface providing common CRUD and pagination operations.
    /// </summary>
    /// <typeparam name="T">Entity type inheriting from BaseEntity</typeparam>
    public interface IBaseService<T> where T : BaseEntity
    {
        public ICrudRepository<T> Repository { get; }
        public Task<ResultWrapper<T?>> GetByIdAsync(Guid id, CancellationToken ct = default);
        public Task<ResultWrapper<T?>> GetOneAsync(FilterDefinition<T> filter, CancellationToken ct = default);
        public Task<ResultWrapper<List<T>>> GetManyAsync(FilterDefinition<T> filter, CancellationToken ct = default);
        public Task<ResultWrapper<List<T>>> GetAllAsync(CancellationToken ct = default);
        public Task<ResultWrapper<PaginatedResult<T>>> GetPaginatedAsync(
            FilterDefinition<T> filter,
            SortDefinition<T> sortDefinition,
            int page = 1,
            int pageSize = 20,
            CancellationToken cancellationToken = default);
        public Task<ResultWrapper<CrudResult<T>>> InsertAsync(T entity, CancellationToken ct = default);
        public Task<ResultWrapper<CrudResult<T>>> UpdateAsync(Guid id, object fields, CancellationToken ct = default);
        public Task<ResultWrapper<CrudResult<T>>> DeleteAsync(Guid id, CancellationToken ct = default);
        public Task<ResultWrapper<CrudResult<T>>> DeleteManyAsync(FilterDefinition<T> filter, CancellationToken ct = default);
        public Task<ResultWrapper<bool>> ExistsAsync(Guid id, CancellationToken cancellationToken = default);
        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<IClientSessionHandle, Task<TResult>> action,
            CancellationToken cancellationToken = default);
    }
}
