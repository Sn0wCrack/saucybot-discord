namespace SaucyBot.Services.Cache;

public class MemoryCacheDriver : ICacheDriver
{
    private readonly Dictionary<object, CacheEntry> _cache = new();

    public Task<object?> Get(object key)
    {
        var entry = _cache[key];

        if (!entry.IsExpired())
        {
            return Task.FromResult(entry.Value);
        }

        _cache.Remove(key);
        return Task.FromResult<>(null);
    }

    public Task<bool> Delete(object key)
    {
        return Task.FromResult(_cache.Remove(key));
    }

    public Task<bool> Set(object key, object value, TimeSpan? expiry)
    {
        var entry = new CacheEntry(value, expiry);
        _cache.Add(key, entry);

        return Task.FromResult(true);
    }
}

public class CacheEntry
{
    public readonly object Value;

    private readonly TimeSpan? _lifetime;

    private readonly DateTimeOffset _createdAt;

    public CacheEntry(object value, TimeSpan? lifetime)
    {
        Value = value;
        _lifetime = lifetime;
        _createdAt = DateTimeOffset.UtcNow;
    }

    public bool IsExpired()
    {
        if (_lifetime is null)
        {
            return false;
        }

        var expiry = _createdAt.Add(_lifetime);
        
        return (expiry >= DateTimeOffset.UtcNow);
    }
}
