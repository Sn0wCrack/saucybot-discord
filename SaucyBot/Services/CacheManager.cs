using SaucyBot.Services.Cache;

namespace SaucyBot.Services;

public class CacheManager : ICacheManager
{
    private readonly ILogger<CacheManager> _logger;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;

    private readonly ICacheDriver _driver;
    
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

    public async Task<T?> Get<T>(object key)
    {
        return await _driver.Get<T>(key);
    }
    
    public async Task<T> Set<T>(object key, T value)
    {
        return await _driver.Set(key, value);
    }

    public async Task<T> Set<T>(object key, T value, TimeSpan expiry)
    {
        return await _driver.Set(key, value, expiry);
    }

    public async Task<bool> Delete(object key)
    {
        return await _driver.Delete(key);
    }

    public async Task<T?> Remember<T>(object key, Func<Task<T?>> value)
    {
        var existing = await Get<T>(key);

        if (existing is not null)
        {
            return existing;
        }

        var store = await value.Invoke();

        if (store is not null)
        {
            await Set<T>(key, store);
        }

        return store;
    }

    public async Task<T?> Remember<T>(object key, TimeSpan expiry, Func<Task<T?>> value)
    {
        var existing = await Get<T>(key);

        if (existing is not null)
        {
            return existing;
        }

        var store = await value.Invoke();

        if (store is not null)
        {
            await Set<T>(key, store, expiry);
        }

        return store;
    }
}
