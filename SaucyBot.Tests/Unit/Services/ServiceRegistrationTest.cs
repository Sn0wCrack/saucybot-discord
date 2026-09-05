using Microsoft.Extensions.DependencyInjection;
using SaucyBot.Commands;
using SaucyBot.Database;
using SaucyBot.Services;
using Xunit;

namespace SaucyBot.Tests.Unit.Services;

public sealed class ServiceRegistrationTest
{
    [Fact]
    public void DatabaseBackedServicesAreRegistered()
    {
        var services = new ServiceCollection();

        services
            .AddSaucyBotDatabase()
            .AddSaucyBotServices();

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(DatabaseContext));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IDatabaseMigrator) &&
            descriptor.ImplementationType == typeof(DatabaseMigrator));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IGuildConfigurationManager) &&
            descriptor.ImplementationType == typeof(GuildConfigurationManager));
    }

    [Fact]
    public void SettingsModuleRegistersWithDatabaseBackedServices()
    {
        var services = new ServiceCollection()
            .AddSaucyBotDatabase()
            .AddSaucyBotServices()
            .BuildServiceProvider();

        Assert.True(InteractionHandler.ShouldRegister(typeof(SettingsModule), services));
    }
}
