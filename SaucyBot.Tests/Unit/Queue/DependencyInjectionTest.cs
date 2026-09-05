using Microsoft.Extensions.DependencyInjection;
using SaucyBot.Diagnostics;
using SaucyBot.Library.Discord;
using SaucyBot.Queue;
using SaucyBot.Services;
using Xunit;

namespace SaucyBot.Tests.Unit.Queue;

public sealed class DependencyInjectionTest
{
    [Fact]
    public void QueueFactoriesAndResolverAreRegisteredAsSingletonInterfaces()
    {
        var services = new ServiceCollection();

        services.AddSaucyBotServices();

        using var provider = services.BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<IMessageWorkItemFactory>(), provider.GetRequiredService<IMessageWorkItemFactory>());
        Assert.Same(provider.GetRequiredService<IInteractionWorkItemFactory>(), provider.GetRequiredService<IInteractionWorkItemFactory>());
        Assert.Same(provider.GetRequiredService<IInteractionDeferrer>(), provider.GetRequiredService<IInteractionDeferrer>());
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
}
