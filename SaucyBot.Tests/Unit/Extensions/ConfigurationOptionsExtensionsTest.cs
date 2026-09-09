using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using SaucyBot.Extensions;
using SaucyBot.Options;
using SaucyBot.Services.Cache;
using Xunit;

namespace SaucyBot.Tests.Unit.Extensions;

public sealed class ConfigurationOptionsExtensionsTest
{
    [Fact]
    public void BindOrDefaultReturnsClassDefaultsWhenTheSectionIsMissing()
    {
        var configuration = new ConfigurationBuilder().Build();

        var options = configuration.BindOrDefault<CacheOptions>("Cache");

        Assert.Equal(CacheDriverType.Memory, options.Driver);
        Assert.Equal(3600, options.Redis.DefaultLifetime);
    }

    [Fact]
    public void BindOrDefaultBindsTheSectionAndKeepsDefaultsForUnsetKeys()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:Driver"] = "Redis",
                ["Cache:Memory:DefaultLifetime"] = "120",
            })
            .Build();

        var options = configuration.BindOrDefault<CacheOptions>("Cache");

        Assert.Equal(CacheDriverType.Redis, options.Driver);
        Assert.Equal(120, options.Memory.DefaultLifetime);
        Assert.Equal(3600, options.Redis.DefaultLifetime);
    }
}
