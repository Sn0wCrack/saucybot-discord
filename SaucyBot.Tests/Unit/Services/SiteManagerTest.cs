using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SaucyBot.Database.Models;
using SaucyBot.Queue;
using SaucyBot.Services;
using SaucyBot.Site;
using SaucyBot.Site.Bluesky;
using Xunit;

namespace SaucyBot.Tests.Unit.Services;

public sealed class SiteManagerTest
{
    [Fact]
    public async Task HandleAsyncPropagatesCancellationFromSiteProcessing()
    {
        using var cancellation = new CancellationTokenSource();
        var site = Substitute.For<IBlueskySite>();
        site.Identifier.Returns("Cancellation");
        site.Pattern.Returns(new Regex("https://example.test"));
        site.Process(Arg.Any<ProcessRequest>())
            .Returns(_ =>
            {
                cancellation.Cancel();
                return Task.FromException<ProcessResponse?>(new OperationCanceledException(cancellation.Token));
            });

        var services = new ServiceCollection();
        services.AddSingleton(site);
        using var provider = services.BuildServiceProvider();
        var registry = new SiteRegistry(
            Substitute.For<ILogger<SiteRegistry>>(),
            new ConfigurationBuilder().Build(),
            provider);

        var resolver = Substitute.For<IMessageResolver>();
        resolver.IsNsfw(Arg.Any<ulong>()).Returns(false);
        var manager = new SiteManager(
            Substitute.For<ILogger<SiteManager>>(),
            new ConfigurationBuilder().Build(),
            new MessageManager(Substitute.For<ILogger<MessageManager>>(), new ConfigurationBuilder().Build()),
            Substitute.For<IGuildConfigurationManager>(),
            registry,
            resolver);
        var item = new MessageWorkItem(
            7,
            0,
            42,
            12,
            [],
            "https://example.test",
            null,
            [],
            true,
            true,
            Guid.NewGuid());

        await Assert.ThrowsAsync<OperationCanceledException>(() => manager.HandleAsync(item, cancellation.Token));
    }
}
