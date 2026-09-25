using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace SaucyBot.Queue.Redis;

public static class ServiceRegistration
{
    public static IServiceCollection AddRedisQueue(this IServiceCollection services, RedisWorkQueueOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        RedisWorkQueueOptionsValidator.Validate(options);

        services.AddSingleton(options);
        services.TryAddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(options.ConnectionString));
        services.AddSingleton<IRedisStreamClient, StackExchangeRedisStreamClient>();
        services.AddSingleton<RedisWorkQueue>();
        services.AddSingleton<IMessageWorkQueue>(provider => provider.GetRequiredService<RedisWorkQueue>());
        services.AddSingleton<IWorkItemProducer<MessageWorkItem>>(provider => provider.GetRequiredService<RedisWorkQueue>());
        services.AddSingleton<IWorkItemConsumer<MessageWorkItem>>(provider => provider.GetRequiredService<RedisWorkQueue>());

        return services;
    }
}
