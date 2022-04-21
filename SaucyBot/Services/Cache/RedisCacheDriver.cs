using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace SaucyBot.Services.Cache;

public class RedisCacheDriver : ICacheDriver
{
    private readonly IDistributedCache _cache;
    private readonly IConfiguration _configuration;

    private readonly TimeSpan _defaultExpiry;

    public RedisCacheDriver(IDistributedCache cache, IConfiguration configuration)
    {
        _cache = cache;
        _configuration = configuration;

        _defaultExpiry = TimeSpan.FromSeconds(
            _configuration.GetSection("Cache:Redis:DefaultLifetime").Get<int>()
        );
    }

    public async Task<T?> Get<T>(object key)
    {
        var value = await _cache.GetStringAsync(key.ToString());

        return value is null ? default : JsonSerializer.Deserialize<T>(value);
    }

    public async Task<bool> Delete(object key)
    {
        await _cache.RemoveAsync(key.ToString());

        return true;
    }

    public async Task<T> Set<T>(object key, T value)
    {
        await _cache.SetStringAsync(
            key.ToString(),
            JsonSerializer.Serialize(value),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _defaultExpiry
            }
        );

        return value;
    }

    public async Task<T> Set<T>(object key, T value, TimeSpan expiry)
    {
        await _cache.SetStringAsync(
            key.ToString(),
            JsonSerializer.Serialize(value),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiry
            }
        );

        return value;
    }
}
