using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using SaucyBot.Diagnostics;
using Xunit;

namespace SaucyBot.Tests.Unit.Diagnostics;

public sealed class TelemetryServiceRegistrationTest
{
    [Fact]
    public void EnabledConfigurationBindsAndRegistersMetricsWithoutTracingByDefault()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenTelemetry:Enabled"] = "true",
                ["OpenTelemetry:ServiceName"] = "test-service",
                ["OpenTelemetry:OtlpEndpoint"] = "http://collector:4318",
                ["OpenTelemetry:OtlpProtocol"] = "HttpProtobuf",
                ["OpenTelemetry:OtlpHeaders"] = "api-key=secret",
                ["OpenTelemetry:ExportIntervalMilliseconds"] = "2500",
                ["OpenTelemetry:Tracing:Enabled"] = "false",
            })
            .Build();
        var services = new ServiceCollection();

        services.AddSaucyBotTelemetry(configuration);
        using var provider = services.BuildServiceProvider();

        var options = configuration.GetSection("OpenTelemetry").Get<TelemetryOptions>();

        Assert.NotNull(options);
        Assert.True(options.Enabled);
        Assert.Equal("test-service", options.ServiceName);
        Assert.Equal("http://collector:4318", options.OtlpEndpoint);
        Assert.Equal("HttpProtobuf", options.OtlpProtocol);
        Assert.Equal("api-key=secret", options.OtlpHeaders);
        Assert.Equal(2500, options.ExportIntervalMilliseconds);
        Assert.False(options.Tracing.Enabled);
        Assert.NotNull(provider.GetService<MeterProvider>());
        Assert.IsType<SaucyBotMetrics>(provider.GetRequiredService<ISaucyBotMetrics>());
        Assert.Null(provider.GetService<TracerProvider>());
    }

    [Fact]
    public void DisabledConfigurationDoesNotRegisterProviders()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenTelemetry:Enabled"] = "false",
            })
            .Build();
        var services = new ServiceCollection();

        services.AddSaucyBotTelemetry(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.Null(provider.GetService<MeterProvider>());
        Assert.Null(provider.GetService<TracerProvider>());
        Assert.IsType<SaucyBotMetrics>(provider.GetRequiredService<ISaucyBotMetrics>());
    }

    [Fact]
    public void EnabledTracingRegistersATracerProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenTelemetry:Enabled"] = "true",
                ["OpenTelemetry:Tracing:Enabled"] = "true",
                ["OpenTelemetry:Tracing:SamplingRatio"] = "0.25",
            })
            .Build();
        var services = new ServiceCollection();

        services.AddSaucyBotTelemetry(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<TracerProvider>());
    }
}
