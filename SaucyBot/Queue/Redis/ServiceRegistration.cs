using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace SaucyBot.Queue.Redis;

public static class ServiceRegistration
{
    public static IServiceCollection AddRedisQueue(
        this IServiceCollection services,
        RedisWorkQueueOptions options,
        TimeSpan commandTimeout)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        RedisWorkQueueOptionsValidator.Validate(options);

        services.AddSingleton(options);
        services.TryAddSingleton<IConnectionMultiplexer>(
            _ => ConnectionMultiplexer.Connect(BuildConfiguration(options, commandTimeout)));
        services.AddSingleton<IRedisStreamClient, StackExchangeRedisStreamClient>();
        services.AddSingleton<RedisWorkQueue>();
        services.AddSingleton<IWorkItemProducer<MessageWorkItem>>(provider => provider.GetRequiredService<RedisWorkQueue>());
        services.AddSingleton<IWorkItemConsumer<MessageWorkItem>>(provider => provider.GetRequiredService<RedisWorkQueue>());

        return services;
    }

    // The Redis-native command timeouts bound every command at the transport on
    // top of the generic backend operation timeout applied at the contract
    // boundary. Both values are set because a connection-string asyncTimeout
    // would otherwise let async commands outlive the operation timeout.
    public static ConfigurationOptions BuildConfiguration(
        RedisWorkQueueOptions options,
        TimeSpan commandTimeout)
    {
        ArgumentNullException.ThrowIfNull(options);

        var configuration = ConfigurationOptions.Parse(options.ConnectionString);
        var timeout = (int)Math.Clamp(commandTimeout.TotalMilliseconds, 1, int.MaxValue);
        configuration.SyncTimeout = timeout;
        configuration.AsyncTimeout = timeout;
        return configuration;
    }
}
