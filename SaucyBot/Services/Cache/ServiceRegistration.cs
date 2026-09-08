using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace SaucyBot.Services.Cache;

public static class CacheServiceRegistration
{
    public static IServiceCollection AddSaucyBotCache(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache(options =>
        {
            options.SizeLimit = configuration.GetSection("Cache:Memory:SizeLimit").Get<long?>();
            options.CompactionPercentage = 0.2;
        });
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = configuration.GetSection("Cache:Redis:ConnectionString").Get<string>();
        });
        services.AddHybridCache();

        services.AddKeyedSingleton<ICacheDriver, MemoryCacheDriver>(CacheDriverType.Memory);
        services.AddKeyedSingleton<ICacheDriver, RedisCacheDriver>(CacheDriverType.Redis);
        services.AddKeyedSingleton<ICacheDriver, HybridCacheDriver>(CacheDriverType.Hybrid);
        services.AddSingleton<ICacheManager, CacheManager>();

        return services;
    }
}
