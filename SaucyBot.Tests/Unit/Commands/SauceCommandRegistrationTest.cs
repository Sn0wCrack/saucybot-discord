using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SaucyBot.Commands;
using Xunit;

namespace SaucyBot.Tests.Unit.Commands;

public class SauceCommandRegistrationTest
{
    [Fact]
    public void SauceCommandShouldRegisterWhenRestrictNsfwIsDisabled()
    {
        var services = CreateServiceProvider(restrictNsfw: false);
        var command = new SauceCommand(null!);

        Assert.True(command.ShouldRegister(services));
    }

    [Fact]
    public void SauceCommandShouldNotRegisterWhenRestrictNsfwIsEnabled()
    {
        var services = CreateServiceProvider(restrictNsfw: true);
        var command = new SauceCommand(null!);

        Assert.False(command.ShouldRegister(services));
    }

    [Fact]
    public void NsfwSauceCommandShouldRegisterWhenRestrictNsfwIsEnabled()
    {
        var services = CreateServiceProvider(restrictNsfw: true);
        var command = new NsfwSauceCommand(null!);

        Assert.True(command.ShouldRegister(services));
    }

    [Fact]
    public void NsfwSauceCommandShouldNotRegisterWhenRestrictNsfwIsDisabled()
    {
        var services = CreateServiceProvider(restrictNsfw: false);
        var command = new NsfwSauceCommand(null!);

        Assert.False(command.ShouldRegister(services));
    }

    [Fact]
    public void NsfwSauceCommandShouldNotRegisterWhenRestrictNsfwIsUnset()
    {
        var services = CreateServiceProvider(restrictNsfw: null);
        var command = new NsfwSauceCommand(null!);

        Assert.False(command.ShouldRegister(services));
    }

    private static ServiceProvider CreateServiceProvider(bool? restrictNsfw)
    {
        var configurationValues = new Dictionary<string, string?>();

        if (restrictNsfw is { } restrictNsfwValue)
        {
            configurationValues["Bot:RestrictNSFW"] = restrictNsfwValue.ToString();
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationValues)
            .Build();

        return new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .BuildServiceProvider();
    }
}
