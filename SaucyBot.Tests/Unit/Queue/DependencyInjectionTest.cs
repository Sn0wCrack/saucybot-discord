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
}
