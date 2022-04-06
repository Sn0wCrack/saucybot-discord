using System.Data.Entity.Core;
using SaucyBot.Services.Cache;

namespace SaucyBot.Services;

public class CacheManager
{
    private readonly ILogger<CacheManager> _logger;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;

    private ICacheDriver _driver;
    
    public CacheManager(ILogger<CacheManager> logger, IConfiguration configuration, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _configuration = configuration;
        _serviceProvider = serviceProvider;

        _driver = CreateDriver();
    }

    private ICacheDriver CreateDriver()
    {
        var driver = _configuration.GetSection("Cache:Driver").Get<string>();

        var driverType = driver.ToLowerInvariant().Trim() switch
        {
            "redis" => typeof(RedisCacheDriver),
            "memory" => typeof(MemoryCacheDriver),
            _ => typeof(MemoryCacheDriver),
        };

        if (_serviceProvider.GetService(driverType) is not ICacheDriver instance)
        {
            throw new Exception($"Unable to create Cache Driver of type {driver}");
        }

        return instance;
    }

    public async Task<object?> Get(object key)
    {
        return await _driver.Get(key);
    }

    public async Task<bool> Set(object key, object value, TimeSpan? expiry)
    {
        return await _driver.Set(key, value, expiry);
    }

    public async Task<bool> Delete(object key)
    {
        return await _driver.Delete(key);
    }

    public async Task<object?> Remember(object key, TimeSpan expiry, Func<object?> value)
    {
        var existing = await _driver.Get(key);

        if (existing is not null)
        {
            return existing;
        }

        var store = value.Invoke();

        if (store is not null)
        {
            await _driver.Set(key, store, expiry);
        }

        return store;
    }
}
