using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SaucyBot.Library.Sites.FurAffinity;
using SaucyBot.Services;
using Xunit;

namespace SaucyBot.Tests.Unit.Library.Sites;

public sealed class FurAffinityServiceRegistrationTest
{
    [Fact]
    public void RegistersOnlyTheActiveFaExportClient()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient();
        services.AddSingleton<ICacheManager>(Substitute.For<ICacheManager>());
        services.AddFurAffinityClient();

        var registrations = services.Where(descriptor => descriptor.ServiceType == typeof(IFaExportClient)).ToArray();

        Assert.Single(registrations);
        using var provider = services.BuildServiceProvider();
        Assert.IsType<FaExportClient>(provider.GetRequiredService<IFaExportClient>());
    }
}
