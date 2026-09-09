using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SaucyBot.Diagnostics;
using Sentry;
using Xunit;
using SentryOptions = SaucyBot.Options.SentryOptions;

namespace SaucyBot.Tests.Unit.Diagnostics;

public sealed class SentryServiceRegistrationTest
{
    [Fact]
    public void SentrySdkIsInitializedWhenADsnIsConfigured()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sentry:Dsn"] = "https://abc@o123456.ingest.sentry.io/4504994854862848",
                ["Sentry:SampleRate"] = "0.25",
            })
            .Build();
        var services = new ServiceCollection();

        services.AddSaucyBotSentry(BindOptions(configuration));

        try
        {
            Assert.True(SentrySdk.IsEnabled);
        }
        finally
        {
            SentrySdk.Close();
        }
    }

    [Fact]
    public void SentrySdkIsNotInitializedWhenNoDsnIsConfigured()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        services.AddSaucyBotSentry(BindOptions(configuration));

        Assert.False(SentrySdk.IsEnabled);
    }

    private static IOptions<SentryOptions> BindOptions(IConfiguration configuration) =>
        Microsoft.Extensions.Options.Options.Create(
            configuration.GetSection("Sentry").Get<SentryOptions>() ?? new SentryOptions());
}
