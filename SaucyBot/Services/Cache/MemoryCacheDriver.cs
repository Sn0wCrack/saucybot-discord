using Microsoft.Extensions.Caching.Memory;

namespace SaucyBot.Services.Cache;

public class MemoryCacheDriver : ICacheDriver
{
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _configuration;
    
    public MemoryCacheDriver(IMemoryCache cache, IConfiguration configuration)
    {
        _cache = cache;
        _configuration = configuration;
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

    public Task<bool> Set<T>(object key, T value, TimeSpan? expiry)
    {
        _cache.Set(key, value);

        return Task.FromResult(true);
    }
}
