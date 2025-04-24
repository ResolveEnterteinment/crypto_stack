using Domain.Models;
namespace Application.Interfaces.Base
{
    public interface ICacheService<T> where T : BaseEntity
    {
        Task<T?> GetCachedEntityAsync(string key, Func<Task<T?>> factory, TimeSpan? duration = null);
        public Task<List<T>?> GetCachedCollectionAsync(string key, Func<Task<List<T>?>> factory, TimeSpan? duration = null);
        Task<TItem?> GetAnyCachedAsync<TItem>(string key, Func<Task<TItem?>> factory, TimeSpan? duration = null);
        void Invalidate(string key);
        string GetCacheKey(Guid id);
        string GetFilterCacheKey();
        string GetCollectionCacheKey();
        public bool TryGetValue<TItem>(object key, out TItem value);
    }
}