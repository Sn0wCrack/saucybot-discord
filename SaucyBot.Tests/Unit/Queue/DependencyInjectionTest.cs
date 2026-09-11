using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SaucyBot.Database;
using SaucyBot.Diagnostics;
using SaucyBot.Library.Discord;
using SaucyBot.Library.Sites;
using SaucyBot.Library.Sites.ArtStation;
using SaucyBot.Library.Sites.BlueSky;
using SaucyBot.Library.Sites.DeviantArt;
using SaucyBot.Library.Sites.E621;
using SaucyBot.Library.Sites.ExHentai;
using SaucyBot.Library.Sites.FurAffinity;
using SaucyBot.Library.Sites.HentaiFoundry;
using SaucyBot.Library.Sites.Misskey;
using SaucyBot.Library.Sites.Newgrounds;
using SaucyBot.Library.Sites.Pixiv;
using SaucyBot.Library.Sites.Twitter;
using SaucyBot.Options;
using SaucyBot.Queue;
using SaucyBot.Services;
using SaucyBot.Services.Cache;
using SaucyBot.Site;
using SaucyBot.Site.Reddit;
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
    public void ProductionWorkerCompositionIsValidWithScopeValidation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([
                new KeyValuePair<string, string?>("Sites:FurAffinity:Cookies:A", "a"),
                new KeyValuePair<string, string?>("Sites:FurAffinity:Cookies:B", "b"),
                new KeyValuePair<string, string?>("Sites:ExHentai:Cookies:MemberId", "member"),
                new KeyValuePair<string, string?>("Sites:ExHentai:Cookies:PasswordHash", "password"),
                new KeyValuePair<string, string?>("Sites:HentaiFoundry:SessionCookie", "session"),
                new KeyValuePair<string, string?>("Sites:Pixiv:SessionCookie", "session"),
            ]).Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.Configure<BotOptions>(configuration.GetSection("Bot"));
        services.AddSaucyBotDatabase();
        services.AddSaucyBotCache(configuration);
        services.AddSaucyBotServices();
        services.AddSaucyBotSites();
        services.AddFurAffinityClient(configuration);
        services.AddArtStationClient();
        services.AddNewgroundsClient();
        services.AddDeviantArtOpenEmbedClient();
        services.AddE621Client();
        services.AddFxTwitterClient();
        services.AddTwitterImageSyndicationClient();
        services.AddMisskeyClient();
        services.AddVixBlueskyClient();
        services.AddPixivClient(configuration);
        services.AddExHentaiClient(configuration);
        services.AddHentaiFoundryClient(configuration);
        services.AddDeviantArtClient();
        services.AddFileDownloadClient();
        services.AddSingleton<IDatabaseMigrator>(Substitute.For<IDatabaseMigrator>());
        services.AddSingleton<IMessageWorkQueue>(Substitute.For<IMessageWorkQueue>());
        services.AddSingleton<IWorkItemProcessor>(Substitute.For<IWorkItemProcessor>());
        services.AddSingleton(new WorkQueueOptions());
        services.AddSingleton<InteractionWorkChannel>();
        services.AddSingleton<WorkQueueHostedService>();
        services.AddSingleton<ISaucyBotMetrics, SaucyBotMetrics>();
        services.AddSingleton<DiscordClientHost>();
        services.AddSingleton<Worker>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        _ = provider.GetRequiredService<Worker>();

        using var scope = provider.CreateScope();
        Assert.IsType<RedditSite>(scope.ServiceProvider.GetRequiredService<SiteRegistry>()
            .Resolve("Reddit", scope.ServiceProvider));
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
