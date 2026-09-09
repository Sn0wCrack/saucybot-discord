using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SaucyBot.Options;

namespace SaucyBot.Services.Cache;

public sealed class MemoryCacheDriver : ICacheDriver
{
    private readonly IMemoryCache _cache;

    private readonly TimeSpan _defaultExpiry;

    public MemoryCacheDriver(IMemoryCache cache, IOptions<CacheOptions> cacheOptions)
    {
        _cache = cache;

        _defaultExpiry = TimeSpan.FromSeconds(
            cacheOptions.Value.Memory.DefaultLifetime
        );
    }

    public Task<T?> Get<T>(object key)
    {
        return Task.FromResult(_cache.Get<T?>(key));
    }

    public Task<bool> Delete(object key)
    {
        _cache.Remove(key);

        return Task.FromResult(true);
    }

    public Task<T> Set<T>(object key, T value)
    {
        var options = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(_defaultExpiry)
            .SetSize(1);

        _cache.Set(key, value, options);

        return Task.FromResult(value);
    }

    public Task<T> Set<T>(object key, T value, TimeSpan expiry)
    {
        var options = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(expiry)
            .SetSize(1);

        _cache.Set(key, value, options);

        return Task.FromResult(value);
    }
}
