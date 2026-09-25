using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using SaucyBot.Diagnostics;
using SaucyBot.Library.Discord;
using SaucyBot.Library.Sites;
using SaucyBot.Options;
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
    public void ResolverAndInteractionProcessorRemainRegistered()
    {
        var services = new ServiceCollection();

        services.AddSaucyBotServices();

        using var provider = services.BuildServiceProvider();

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IInteractionProcessor));
        Assert.Same(provider.GetRequiredService<IMessageResolver>(), provider.GetRequiredService<IMessageResolver>());
    }

    [Fact]
    public void MetricsInterfaceResolvesToTheSingletonMetricsImplementation()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISaucyBotMetrics, SaucyBotMetrics>();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<SaucyBotMetrics>(provider.GetRequiredService<ISaucyBotMetrics>());
        Assert.Same(provider.GetRequiredService<ISaucyBotMetrics>(), provider.GetRequiredService<ISaucyBotMetrics>());
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
        Assert.Same(producer, provider.GetRequiredService<IMessageWorkQueue>());
        Assert.Same(producer, provider.GetRequiredService<RedisWorkQueue>());
    }

    [Fact]
    public void RedisQueueExtensionRejectsInvalidBackendOptions()
    {
        var services = new ServiceCollection();

        Assert.Throws<OptionsValidationException>(() =>
            services.AddRedisQueue(
                new RedisWorkQueueOptions { PendingReadTimeout = TimeSpan.Zero },
                TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void SiteManagerRequiresAServiceProviderForQueuedProcessing()
    {
        var constructor = typeof(SiteManager).GetConstructors().Single();
        var resolver = constructor.GetParameters().Single(parameter => parameter.ParameterType == typeof(IServiceProvider));

        Assert.False(resolver.HasDefaultValue);
    }

    [Fact]
    public void InteractionProcessorReliesOnTheFrameworkForScoping()
    {
        var constructor = typeof(InteractionProcessor).GetConstructors().Single();

        Assert.Contains(constructor.GetParameters(), parameter => parameter.ParameterType == typeof(InteractionHandler));
        Assert.DoesNotContain(constructor.GetParameters(), parameter => parameter.ParameterType == typeof(IServiceScopeFactory));
        Assert.DoesNotContain(constructor.GetParameters(), parameter => parameter.ParameterType == typeof(IServiceProvider));
    }

    [Fact]
    public void SiteImplementationsAreScopedWithTheirTypedClients()
    {
        var services = new ServiceCollection();

        services.AddSaucyBotSites();

        var siteRegistrations = services
            .Where(descriptor => descriptor.ServiceType == typeof(SiteRegistration))
            .Select(descriptor => ((SiteRegistration)descriptor.ImplementationInstance!).ImplementationType)
            .ToHashSet();

        Assert.NotEmpty(siteRegistrations);
        Assert.All(siteRegistrations, siteType => Assert.Equal(
            ServiceLifetime.Scoped,
            services.Single(descriptor => descriptor.ServiceType == siteType).Lifetime));
    }

    [Fact]
    public void WorkerAdmissionUsesSingletonSiteMetadataAndScopedResolution()
    {
        var services = new ServiceCollection();
        services.AddSaucyBotServices();

        Assert.Equal(ServiceLifetime.Singleton,
            services.Single(descriptor => descriptor.ServiceType == typeof(SiteRegistry)).Lifetime);
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType.Name == "ISiteResolver");
        Assert.DoesNotContain(typeof(Worker).GetConstructors().Single().GetParameters(),
            parameter => parameter.ParameterType == typeof(SiteManager));
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
