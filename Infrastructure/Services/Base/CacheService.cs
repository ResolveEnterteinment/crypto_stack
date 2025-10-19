using Application.Interfaces.Base;
using Application.Interfaces.Logging;
using Microsoft.Extensions.Caching.Memory;
using MongoDB.Bson;
using System.Collections.Concurrent;

public class CacheService<T> : ICacheService<T> where T : class
{
    private readonly IMemoryCache _cache;
    private readonly ILoggingService _logger;
    private readonly ConcurrentDictionary<string, byte> _trackedKeys = new();
    private static readonly TimeSpan DefaultDuration = TimeSpan.FromMinutes(5);

    public CacheService(
        IMemoryCache cache,
        ILoggingService logger)
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
            _logger.LogInformation("Cache {Action} for {Type}:{Key}", "hit", typeof(T).Name, key);
            return cached;
        }

        _logger.LogInformation("Cache {Action} for {Type}:{Key}", "missed", typeof(T).Name, key);
        var item = await factory();
        if (item is not null)
        {
            _cache.Set(key, item, duration ?? DefaultDuration);
        }

        return item;
    }

    public async Task<List<T>?> GetCachedCollectionAsync(
        string key,
        Func<Task<List<T>?>> factory,
        TimeSpan? duration = null)
    {
        if (_cache.TryGetValue(key, out List<T>? cached))
        {
            _logger.LogInformation("Cache {Action} for {Type}:{Key}", "hit", typeof(T).Name, key);
            return cached!;
        }

        _logger.LogInformation("Cache {Action} for {Type}:{Key}", "missed", typeof(T).Name, key);

        var collection = await factory();
        if (collection is not null)
        {
            _cache.Set(key, collection, duration ?? DefaultDuration);
        }

        return collection;
    }

    public async Task<TItem?> GetAnyCachedAsync<TItem>(
        string key,
        Func<Task<TItem?>> factory,
        TimeSpan? duration = null)
    {
        if (_cache.TryGetValue(key, out TItem? cached))
        {
            _logger.LogInformation("Cache {Action} for {Type}: {Key}", "hit", typeof(T).Name, key);
            return cached;
        }

        _logger.LogInformation("Cache {Action} for {Type}: {Key}", "missed", typeof(T).Name, key);
        var item = await factory();
        if (item is not null)
        {
            _cache.Set(key, item, duration ?? DefaultDuration);
        }

        return item;
    }

    public void Invalidate(string key)
    {
        _logger.LogInformation("Cache {Action} for {Type}:{Key}", "invalidated", typeof(T).Name, key);
        _cache.Remove(key);
    }

    public void InvalidateWithPrefix(string keyPrefix)
    {
        var keysToRemove = _trackedKeys.Keys.Where(k => k.StartsWith(keyPrefix)).ToList();

        foreach (var key in keysToRemove)
        {
            _cache.Remove(key);
            _trackedKeys.TryRemove(_trackedKeys.First(kvp => kvp.Key.Equals(key)));
            _logger.LogInformation("Cache {Action} for {Type}:{Key}", "invalidated", typeof(T).Name, key);
        }
    }

    public string GetCacheKey(Guid id)
        => $"{typeof(T).Name}:{id}";

    public string GetFilterCacheKey()
        => $"{typeof(T).Name}:filter";

    public string GetCollectionCacheKey()
        => $"{typeof(T).Name}:collection";

    public bool TryGetValue<TItem>(object key, out TItem value)
        => _cache.TryGetValue(key, out value);

    public TItem Set<TItem>(string key, TItem value, TimeSpan? duration = null)
    {
        _trackedKeys.TryAdd(key, byte.MinValue);
        TItem result = _cache.Set(key, value, duration ?? DefaultDuration);
        _logger.LogInformation($"Cache {key} set to {value.ToJson()}");
        return result;
    }
}
