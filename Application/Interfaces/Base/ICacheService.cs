using Domain.Models;
namespace Application.Interfaces.Base
{
    public interface ICacheService<T> where T : class
    {
        Task<T?> GetCachedEntityAsync(string key, Func<Task<T?>> factory, TimeSpan? duration = null);
        public Task<List<T>?> GetCachedCollectionAsync(string key, Func<Task<List<T>?>> factory, TimeSpan? duration = null);
        Task<TItem?> GetAnyCachedAsync<TItem>(string key, Func<Task<TItem?>> factory, TimeSpan? duration = null);
        void Invalidate(string key);
        void InvalidateWithPrefix(string keyPrefix);
        string GetCacheKey(Guid id);
        string GetFilterCacheKey();
        string GetCollectionCacheKey();
        public bool TryGetValue<TItem>(object key, out TItem value);
        TItem Set<TItem>(string key, TItem value, TimeSpan? duration = null);
    }
}