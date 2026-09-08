using Microsoft.Extensions.Options;
using SaucyBot.Options;
using SaucyBot.Services.Cache;

namespace SaucyBot.Services;

public sealed class CacheManager : ICacheManager
{
    private readonly ILogger<CacheManager> _logger;
    private readonly IServiceProvider _serviceProvider;

    private readonly ICacheDriver _driver;

    public CacheManager(ILogger<CacheManager> logger, IOptions<CacheOptions> cacheOptions, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;

        _driver = CreateDriver(cacheOptions.Value);
    }

    private ICacheDriver CreateDriver(CacheOptions cacheOptions)
    {
        var driver = cacheOptions.Driver ?? CacheDriverType.Memory;

        return _serviceProvider.GetKeyedService<ICacheDriver>(driver)
               ?? throw new InvalidOperationException(
                    $"Cache driver '{driver}' is not registered.");
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
            _logger.LogDebug("Found existing cache item with key: {Key}", key);
            return existing;
        }

        var store = await value.Invoke();

        if (store is null)
        {
            return store;
        }

        _logger.LogDebug("Setting cache item with key: {Key} and value: {Value}", key, store);
        await Set<T>(key, store);

        return store;
    }

    public async Task<T?> Remember<T>(object key, TimeSpan expiry, Func<Task<T?>> value)
    {
        var existing = await Get<T>(key);

        if (existing is not null)
        {
            _logger.LogDebug("Found existing cache item with key: {Key}", key);
            return existing;
        }

        var store = await value.Invoke();

        if (store is null)
        {
            return store;
        }

        _logger.LogDebug("Setting cache item with key: {Key} and value: {Value} and expiry: {Expiry}", key, store, expiry);
        await Set<T>(key, store, expiry);

        return store;
    }
}
