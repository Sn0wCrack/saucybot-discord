using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SaucyBot.Queue.Redis;

namespace SaucyBot.Queue;

public static class QueueServiceRegistration
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddSaucyBotQueue(IConfiguration configuration)
        {
            var queueOptions = configuration.GetSection("Queue").Get<WorkQueueOptions>()
                               ?? new WorkQueueOptions();

            if (queueOptions.Driver != QueueDriverType.Redis)
            {
                throw new InvalidOperationException($"Unsupported queue driver: {queueOptions.Driver}");
            }

            services.AddSingleton(queueOptions);
            services.AddSingleton<InteractionWorkChannel>();
            services.AddRedisQueue(
                configuration.GetSection("Queue:Redis").Get<RedisWorkQueueOptions>() ?? new RedisWorkQueueOptions(),
                queueOptions.BackendOperationTimeout);
            services.AddSingleton<IWorkItemProcessor, WorkItemProcessor>();
            services.AddSingleton<IInteractionProcessor, InteractionProcessor>();
            services.AddSingleton<MessageDeliveryChannel>();
            services.AddSingleton<MessageQueueReader>();
            services.AddSingleton<MessageRecoveryWorker>();
            services.AddSingleton<MessageQueueWorker>();
            services.AddSingleton<InteractionQueueWorker>();
            services.AddQueueMiddleware<MessageWorkItem, QueueMetricsMiddleware<MessageWorkItem>>();
            services.AddQueueMiddleware<IInteractionWorkItem, QueueMetricsMiddleware<IInteractionWorkItem>>();
            services.AddSingleton<WorkQueueHostedService>();
            services.AddHostedService(provider => provider.GetRequiredService<WorkQueueHostedService>());

            return services;
        }

        /// <summary>
        /// Registers a middleware module for one work type. Modules register their
        /// middleware here without changes to <see cref="QueueMiddlewarePipeline{T}"/>.
        /// The first call for a work type also registers its pipeline.
        /// </summary>
        public IServiceCollection AddQueueMiddleware<TWork, TMiddleware>()
            where TMiddleware : class, IQueueMiddleware<TWork>
        {
            services.TryAddSingleton<QueueMiddlewarePipeline<TWork>>();
            services.AddSingleton<IQueueMiddleware<TWork>, TMiddleware>();
            return services;
        }
    }
}
