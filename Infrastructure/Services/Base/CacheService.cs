using Application.Interfaces.Base;
using Domain.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

public class CacheService<T> : ICacheService<T> where T : BaseEntity
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<CacheService<T>> _logger;
    private static readonly TimeSpan DefaultDuration = TimeSpan.FromMinutes(5);

    public CacheService(
        IMemoryCache cache,
        ILogger<CacheService<T>> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<T?> GetCachedEntityAsync(
        string key,
        Func<Task<T?>> factory,
        TimeSpan? duration = null)
    {
        if (_cache.TryGetValue(key, out T? cached))
        {
            _logger.LogDebug("Cache {Action} for {Type}:{Key}", "hit", typeof(T).Name, key);
            return cached;
        }

        _logger.LogDebug("Cache {Action} for {Type}:{Key}", "missed", typeof(T).Name, key);
        var item = await factory();
        if (item is not null)
            _cache.Set(key, item, duration ?? DefaultDuration);

        return item;
    }

    public async Task<List<T>?> GetCachedCollectionAsync(
        string key,
        Func<Task<List<T>?>> factory,
        TimeSpan? duration = null)
    {
        if (_cache.TryGetValue(key, out List<T>? cached))
        {
            _logger.LogDebug("Cache {Action} for {Type}:{Key}", "hit", typeof(T).Name, key);
            return cached!;
        }

        _logger.LogDebug("Cache {Action} for {Type}:{Key}", "missed", typeof(T).Name, key);

        var collection = await factory();
        if (collection is not null)
            _cache.Set(key, collection, duration ?? DefaultDuration);

        return collection;
    }

    public async Task<TItem?> GetAnyCachedAsync<TItem>(
        string key,
        Func<Task<TItem?>> factory,
        TimeSpan? duration = null)
    {
        if (_cache.TryGetValue(key, out TItem? cached))
        {
            _logger.LogDebug("Cache {Action} for {Type}:{Key}", "hit", typeof(T).Name, key);
            return cached;
        }

        _logger.LogDebug("Cache {Action} for {Type}:{Key}", "missed", typeof(T).Name, key);
        var item = await factory();
        if (item is not null)
            _cache.Set(key, item, duration ?? DefaultDuration);

        return item;
    }

    public void Invalidate(string key)
    {
        _logger.LogDebug("Cache {Action} for {Type}:{Key}", "invalidated", typeof(T).Name, key);

        _cache.Remove(key);
    }

    public string GetCacheKey(Guid id)
        => $"{typeof(T).Name}:{id}";

    public string GetFilterCacheKey()
        => $"{typeof(T).Name}:filter";

    public string GetCollectionCacheKey()
        => $"{typeof(T).Name}:collection";

    public bool TryGetValue<TItem>(object key, out TItem value)
        => _cache.TryGetValue(key, out value);
}
