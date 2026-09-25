using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SaucyBot.Queue;

public static class QueueServiceRegistration
{
    /// <summary>
    /// Registers a middleware module for one work type. Modules register their
    /// middleware here without changes to <see cref="QueueMiddlewarePipeline{T}"/>.
    /// The first call for a work type also registers its pipeline.
    /// </summary>
    public static IServiceCollection AddQueueMiddleware<TWork, TMiddleware>(this IServiceCollection services)
        where TMiddleware : class, IQueueMiddleware<TWork>
    {
        services.TryAddSingleton<IQueueMiddlewarePipeline<TWork>, QueueMiddlewarePipeline<TWork>>();
        services.AddSingleton<IQueueMiddleware<TWork>, TMiddleware>();
        return services;
    }
}
