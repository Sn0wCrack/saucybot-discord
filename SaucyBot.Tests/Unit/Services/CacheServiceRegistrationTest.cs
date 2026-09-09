using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SaucyBot.Services;
using SaucyBot.Services.Cache;
using Xunit;

namespace SaucyBot.Tests.Unit.Services;

public sealed class CacheServiceRegistrationTest
{
    [Fact]
    public void CacheDriversAreRegisteredAsTypedNamedStrategies()
    {
        var services = new ServiceCollection();

        services.AddSaucyBotCache(new ConfigurationBuilder().Build());

        var registrations = services
            .Where(descriptor => descriptor.ServiceType == typeof(ICacheDriver))
            .Select(descriptor => (descriptor.ServiceKey, descriptor.KeyedImplementationType))
            .ToList();

        Assert.Equal(
            [
                ("Memory", typeof(MemoryCacheDriver)),
                ("Redis", typeof(RedisCacheDriver)),
                ("Hybrid", typeof(HybridCacheDriver)),
            ],
            registrations.Select(registration => (registration.ServiceKey?.ToString(), registration.KeyedImplementationType)));
    }

    [Fact]
    public void CacheDriverConfigurationIsNormalizedBeforeKeyedResolution()
    {
        var driver = Substitute.For<ICacheDriver>();
        var provider = Substitute.For<IKeyedServiceProvider>();
        provider.GetKeyedService(typeof(ICacheDriver), CacheDriverType.Redis).Returns(driver);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("Cache:Driver", "  REDIS ")])
            .Build();

        var manager = new CacheManager(
            Substitute.For<ILogger<CacheManager>>(),
            configuration.CacheOptions(),
            provider);

        _ = manager.Get<string>("key");

        provider.Received(1).GetKeyedService(typeof(ICacheDriver), CacheDriverType.Redis);
    }

    [Fact]
    public void UnknownCacheDriverFailsClearly()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("Cache:Driver", "filesystem")])
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(() => new CacheManager(
            Substitute.For<ILogger<CacheManager>>(),
            configuration.CacheOptions(),
            Substitute.For<IKeyedServiceProvider>()));

        Assert.Contains("filesystem", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingCacheDriverRegistrationFailsClearly()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("Cache:Driver", "redis")])
            .Build();
        var provider = Substitute.For<IKeyedServiceProvider>();

        var exception = Assert.Throws<InvalidOperationException>(() => new CacheManager(
            Substitute.For<ILogger<CacheManager>>(),
            configuration.CacheOptions(),
            provider));

        Assert.Contains("redis", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not registered", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CacheDriverTypeIsTheSingleSourceOfAcceptedDriverValues()
    {
        var driverType = typeof(CacheManager).Assembly.GetType("SaucyBot.Services.Cache.CacheDriverType");

        Assert.NotNull(driverType);
        Assert.True(driverType!.IsEnum);
        Assert.Equal(["Memory", "Redis", "Hybrid"], Enum.GetNames(driverType));
    }
}
