using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;

namespace SaucyBot.Services.Cache;

public sealed class HybridCacheDriver : ICacheDriver
{
    private readonly HybridCache _cache;
    private readonly IConfiguration _configuration;

    private readonly TimeSpan _defaultExpiry;

    public HybridCacheDriver(HybridCache cache, IConfiguration configuration)
    {
        _cache = cache;
        _configuration = configuration;

        _defaultExpiry = TimeSpan.FromSeconds(
            _configuration.GetSection("Cache:Hybrid:DefaultLifetime").Get<int>()
        );
    }

    public async Task<T?> Get<T>(object key)
    {
        var keyAsString = key.ToString();

        if (keyAsString is null)
        {
            throw new Exception("Key could not be converted to a string correctly");
        }

        var options = new HybridCacheEntryOptions
        {
            Flags = HybridCacheEntryFlags.DisableLocalCacheWrite
                    | HybridCacheEntryFlags.DisableDistributedCacheWrite
        };

        var value = await _cache.GetOrCreateAsync<string?>(
            keyAsString,
            factory: static async token => null,
            options: options
        );

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

        await _cache.SetAsync(
            keyAsString,
            JsonSerializer.Serialize(value),
            new HybridCacheEntryOptions
            {
                Expiration = expiry,
                LocalCacheExpiration = expiry.Divide(2),
            }
        );

        return value;
    }
}

