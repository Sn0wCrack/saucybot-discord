using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SaucyBot.Diagnostics;
using SaucyBot.Library.Sites;
using SaucyBot.Queue;
using SaucyBot.Queue.Redis;
using SaucyBot.Services;
using SaucyBot.Site;
using StackExchange.Redis;
using Xunit;

namespace SaucyBot.Tests.Unit.Queue;

public sealed class DependencyInjectionTest
{
    [Fact]
    public void QueueExtensionRegistersTheWorkerSystem()
    {
        var services = new ServiceCollection();
        services.AddSaucyBotQueue(new ConfigurationBuilder().Build());

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(WorkQueueHostedService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IHostedService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkItemProducer<MessageWorkItem>));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkItemConsumer<MessageWorkItem>));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkItemProcessor) &&
            descriptor.ImplementationType == typeof(WorkItemProcessor));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IInteractionProcessor) &&
            descriptor.ImplementationType == typeof(InteractionProcessor));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IQueueMiddleware<MessageWorkItem>));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IQueueMiddleware<IInteractionWorkItem>));
    }

    [Fact]
    public void RedisQueueExtensionRegistersBackendNeutralProducerAndConsumer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IConnectionMultiplexer>());
        services.AddSingleton<ISaucyBotMetrics, SaucyBotMetrics>();
        services.AddSingleton(new WorkQueueOptions());
        services.AddRedisQueue(
            new RedisWorkQueueOptions { ConnectionString = "queue:6379" },
            TimeSpan.FromSeconds(5));

        using var provider = services.BuildServiceProvider();

        var producer = provider.GetRequiredService<IWorkItemProducer<MessageWorkItem>>();
        var consumer = provider.GetRequiredService<IWorkItemConsumer<MessageWorkItem>>();

        Assert.IsType<RedisWorkQueue>(producer);
        Assert.Same(producer, consumer);
        Assert.Same(producer, provider.GetRequiredService<IWorkItemConsumer<MessageWorkItem>>());
        Assert.Same(producer, provider.GetRequiredService<IWorkItemProducer<MessageWorkItem>>());
        Assert.Same(producer, provider.GetRequiredService<RedisWorkQueue>());
    }

    [Fact]
    public void SiteRegistrySkipsDisabledSitesBeforeResolvingHandlers()
    {
        PatternSite.Reset();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("Bot:DisabledSites:0", "Pattern")])
            .Build();
        var services = new ServiceCollection()
            .AddScoped<PatternSite>();

        using var provider = services.BuildServiceProvider();
        var registry = new SiteRegistry(
            SubstituteLogger<SiteRegistry>(),
            configuration.BotOptions(),
            provider,
            [new SiteRegistration(typeof(PatternSite))]);

        Assert.False(registry.HasMatch("https://pattern.test"));
        Assert.Equal(0, PatternSite.ConstructionCount);
    }

    private static ILogger<T> SubstituteLogger<T>() =>
        Microsoft.Extensions.Logging.Abstractions.NullLogger<T>.Instance;

    [SiteIdentifier("Pattern")]
    private sealed class PatternSite : IBaseSite
    {
        public PatternSite() => ConstructionCount++;

        public static int ConstructionCount { get; private set; }
        public string Identifier => "Pattern";
        public Regex Pattern { get; } = new("https://pattern\\.test");
        public Discord.Color Color => Discord.Color.Default;
        public Task<ProcessResponse?> Process(ProcessRequest request) => Task.FromResult<ProcessResponse?>(null);

        public static void Reset() => ConstructionCount = 0;
    }
}
