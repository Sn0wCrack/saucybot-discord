using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SaucyBot.Services;
using SaucyBot.Site;
using SaucyBot.Site.ArtStation;
using SaucyBot.Site.Twitter;
using Xunit;

namespace SaucyBot.Tests.Unit.Services;

public sealed class SiteRegistrationTest
{
    [Fact]
    public void DisabledSiteIsNotConstructed()
    {
        ConstructedSite.Reset();
        var services = new ServiceCollection()
            .AddSingleton<ConstructedSite>()
            .AddSingleton(new SiteRegistration(typeof(ConstructedSite)));

        using var provider = services.BuildServiceProvider();

        _ = new SiteRegistry(
            SubstituteLogger(),
            Configuration([new KeyValuePair<string, string?>("Bot:DisabledSites:0", "disabled")]),
            provider,
            [new SiteRegistration(typeof(ConstructedSite))]);

        Assert.Equal(0, ConstructedSite.ConstructionCount);
    }

    [Fact]
    public void DuplicateSiteIdentifiersFailWithAStartupError()
    {
        var services = new ServiceCollection()
            .AddSingleton<FirstSite>()
            .AddSingleton<SecondSite>()
            .AddSingleton(new SiteRegistration(typeof(FirstSite)))
            .AddSingleton(new SiteRegistration(typeof(SecondSite)));
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(() => new SiteRegistry(
            SubstituteLogger(),
            new ConfigurationBuilder().Build(),
            provider,
            [
                new SiteRegistration(typeof(FirstSite)),
                new SiteRegistration(typeof(SecondSite)),
            ]));

        Assert.Contains("duplicate", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingSiteRegistrationFailsWithAStartupError()
    {
        var services = new ServiceCollection()
            .AddSingleton(new SiteRegistration(typeof(MissingSite)));
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(() => new SiteRegistry(
            SubstituteLogger(),
            new ConfigurationBuilder().Build(),
            provider,
            [new SiteRegistration(typeof(MissingSite))]));

        Assert.Contains("missing", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProductionSiteRegistrationsUseTheirAttributedIdentifiers()
    {
        var services = new ServiceCollection().AddSaucyBotSites();
        var registrations = services
            .Where(descriptor => descriptor.ServiceType == typeof(SiteRegistration))
            .Select(descriptor => (SiteRegistration)descriptor.ImplementationInstance!)
            .ToList();
        var configurationValues = registrations
            .Select((registration, index) => new KeyValuePair<string, string?>($"Bot:DisabledSites:{index}",
                registration.ImplementationType.GetCustomAttributes(typeof(SiteIdentifierAttribute), false)
                    .Cast<SiteIdentifierAttribute>().Single().Identifier));
        using var provider = services.BuildServiceProvider();

        var registry = new SiteRegistry(
            SubstituteLogger(),
            Configuration(configurationValues),
            provider,
            provider.GetServices<SiteRegistration>());

        Assert.Empty(registry.Sites);
        Assert.Equal("FxTwitter", typeof(FxTwitterSite).GetCustomAttributes(typeof(SiteIdentifierAttribute), false)
            .Cast<SiteIdentifierAttribute>().Single().Identifier);
        Assert.Equal("ArtStation", typeof(ArtStationSite).GetCustomAttributes(typeof(SiteIdentifierAttribute), false)
            .Cast<SiteIdentifierAttribute>().Single().Identifier);
    }

    [Fact]
    public void MatchingUsesStartupMetadataWithoutConstructingHandlersPerCandidate()
    {
        ConstructedSite.Reset();
        var services = new ServiceCollection().AddScoped<ConstructedSite>();
        using var provider = services.BuildServiceProvider();
        var registry = new SiteRegistry(
            SubstituteLogger(),
            new ConfigurationBuilder().Build(),
            provider,
            [new SiteRegistration(typeof(ConstructedSite))]);

        Assert.True(registry.HasMatch("test"));
        Assert.True(registry.HasMatch("test"));
        Assert.Equal(1, ConstructedSite.ConstructionCount);
    }

    [Fact]
    public void ResolveUsesThePassedScopedProvider()
    {
        ConstructedSite.Reset();
        var services = new ServiceCollection().AddScoped<ConstructedSite>();
        using var rootProvider = services.BuildServiceProvider();
        var registry = new SiteRegistry(
            SubstituteLogger(),
            new ConfigurationBuilder().Build(),
            rootProvider,
            [new SiteRegistration(typeof(ConstructedSite))]);

        using var scope = rootProvider.CreateScope();
        var resolved = registry.Resolve("disabled", scope.ServiceProvider);

        Assert.Same(resolved, registry.Resolve("disabled", scope.ServiceProvider));
        Assert.Equal(2, ConstructedSite.ConstructionCount);
    }

    [Fact]
    public void ResolveReportsUnknownSiteIdentifiers()
    {
        var registry = new SiteRegistry(
            SubstituteLogger(),
            new ConfigurationBuilder().Build(),
            new ServiceCollection().BuildServiceProvider(),
            []);

        var exception = Assert.Throws<KeyNotFoundException>(() => registry.Resolve(
            "unknown",
            new ServiceCollection().BuildServiceProvider()));

        Assert.Contains("unknown", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ILogger<SiteRegistry> SubstituteLogger() =>
        NSubstitute.Substitute.For<ILogger<SiteRegistry>>();

    private static IConfiguration Configuration(IEnumerable<KeyValuePair<string, string?>> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [SiteIdentifier("disabled")]
    private sealed class ConstructedSite : TestSite
    {
        public static int ConstructionCount { get; private set; }

        public ConstructedSite()
        {
            ConstructionCount++;
        }

        public static void Reset() => ConstructionCount = 0;

        public override string Identifier => "disabled";
    }

    [SiteIdentifier("duplicate")]
    private sealed class FirstSite : TestSite
    {
        public override string Identifier => "duplicate";
    }

    [SiteIdentifier("duplicate")]
    private sealed class SecondSite : TestSite
    {
        public override string Identifier => "duplicate";
    }

    [SiteIdentifier("missing")]
    private sealed class MissingSite : TestSite { }

    private abstract class TestSite : IBaseSite
    {
        public virtual string Identifier => "test";
        public Regex Pattern { get; } = new("test");
        public Discord.Color Color => Discord.Color.Default;
        public Task<ProcessResponse?> Process(ProcessRequest request) => Task.FromResult<ProcessResponse?>(null);
    }
}
