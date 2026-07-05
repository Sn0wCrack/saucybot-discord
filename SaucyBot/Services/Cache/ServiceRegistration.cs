using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

        services.AddSingleton<MemoryCacheDriver>();
        services.AddSingleton<RedisCacheDriver>();
        services.AddSingleton<ICacheManager, CacheManager>();

        return services;
    }
}
