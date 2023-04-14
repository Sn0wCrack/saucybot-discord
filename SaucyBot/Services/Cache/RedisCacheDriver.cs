using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace SaucyBot.Services.Cache;

public sealed class RedisCacheDriver : ICacheDriver
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
        var keyAsString = key.ToString();

        if (keyAsString is null)
        {
            throw new Exception("Key could not be converted to a string correctly");
        }
        
        var value = await _cache.GetStringAsync(keyAsString);

        return value is null ? default : JsonSerializer.Deserialize<T>(value);
    }

    public async Task<bool> Delete(object key)
    {
        var keyAsString = key.ToString();

        if (keyAsString is null)
        {
            throw new Exception("Key could not be converted to a string correctly");
        }
        
        await _cache.RemoveAsync(keyAsString);

        return true;
    }

    public async Task<T> Set<T>(object key, T value)
    {
        var keyAsString = key.ToString();

        if (keyAsString is null)
        {
            throw new Exception("Key could not be converted to a string correctly");
        }
        
        await _cache.SetStringAsync(
            keyAsString,
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
        var keyAsString = key.ToString();

        if (keyAsString is null)
        {
            throw new Exception("Key could not be converted to a string correctly");
        }
        
        await _cache.SetStringAsync(
            keyAsString,
            JsonSerializer.Serialize(value),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiry
            }
        );

        return value;
    }
}
