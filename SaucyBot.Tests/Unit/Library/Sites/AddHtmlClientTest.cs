using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using SaucyBot.Library.Sites;
using Xunit;

namespace SaucyBot.Tests.Unit.Library.Sites;

public class AddHtmlClientTest
{
    [Fact]
    public async Task EachFactoryHandlerEntryGetsItsOwnDistinctPrimaryHandler()
    {
        var instances = new List<HttpClientHandler>();

        var services = new ServiceCollection();
        services.AddHtmlClient<ITestClient, TestClient>(() =>
        {
            var handler = new HttpClientHandler();
            instances.Add(handler);
            return handler;
        });
        services.Configure<HttpClientFactoryOptions>(typeof(ITestClient).Name!, options =>
            options.HandlerLifetime = TimeSpan.FromSeconds(1));

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpMessageHandlerFactory>();

        var name = typeof(ITestClient).Name!;

        factory.CreateHandler(name);
        Assert.Single(instances);

        await Task.Delay(TimeSpan.FromMilliseconds(1500), TestContext.Current.CancellationToken);

        factory.CreateHandler(name);
        Assert.Equal(2, instances.Count);
        Assert.NotSame(instances[0], instances[1]);
    }
}

public interface ITestClient
{
    Task<string> GetAsync();
}

public class TestClient : ITestClient
{
    private readonly HttpClient _client;

    public TestClient(HttpClient client)
    {
        _client = client;
    }

    public Task<string> GetAsync() => _client.GetStringAsync("https://example.test");
}
