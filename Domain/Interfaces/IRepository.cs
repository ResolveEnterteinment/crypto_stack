using Domain.DTOs;
using MongoDB.Driver;

public interface IRepository<T> where T : class
{
    public Task<ResultWrapper<T?>> GetByIdAsync(Guid id, CancellationToken ct = default);
    public Task<ResultWrapper<T?>> GetOneAsync(FilterDefinition<T> filter, CancellationToken ct = default);
    public Task<ResultWrapper<List<T>?>> GetAllAsync(CancellationToken ct = default);
    public Task<ResultWrapper<List<T>?>> GetManyAsync(FilterDefinition<T> filter, CancellationToken ct = default);
    public Task<ResultWrapper> InsertAsync(T entity, CancellationToken ct = default);
    public Task<ResultWrapper> DeleteAsync(Guid id, CancellationToken ct = default);
}
