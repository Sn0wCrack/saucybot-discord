using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using SaucyBot.Options;

namespace SaucyBot.Services.Cache;

public sealed class RedisCacheDriver : ICacheDriver
{
    private readonly IDistributedCache _cache;

    private readonly TimeSpan _defaultExpiry;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.General)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        WriteIndented = false
    };

    public RedisCacheDriver(IDistributedCache cache, IOptions<CacheOptions> cacheOptions)
    {
        _cache = cache;

        _defaultExpiry = TimeSpan.FromSeconds(
            cacheOptions.Value.Redis.DefaultLifetime
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
        return await Set(key, value, _defaultExpiry);
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
            JsonSerializer.Serialize(value, SerializerOptions),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiry
            }
        );

        return value;
    }
}
