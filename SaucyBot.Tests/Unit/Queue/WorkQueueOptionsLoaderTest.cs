using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SaucyBot.Queue;
using Xunit;

namespace SaucyBot.Tests.Unit.Queue;

public sealed class WorkQueueOptionsLoaderTest
{
    [Fact]
    public void Bind_WithNoConfiguration_ReturnsGenericDefaults()
    {
        var options = Bind();

        Assert.Equal(TimeSpan.FromSeconds(5), options.HeartbeatInterval);
        Assert.Equal(TimeSpan.FromSeconds(5), options.ReclaimerInterval);
        Assert.Equal(TimeSpan.FromSeconds(30), options.PendingMessageIdleTime);
    }

    [Fact]
    public void Bind_WithGenericTimingKeys_UsesGenericValues()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Queue:HeartbeatInterval"] = "00:00:07",
            ["Queue:ReclaimerInterval"] = "00:00:09",
            ["Queue:PendingMessageIdleTime"] = "00:00:41",
        });

        Assert.Equal(TimeSpan.FromSeconds(7), options.HeartbeatInterval);
        Assert.Equal(TimeSpan.FromSeconds(9), options.ReclaimerInterval);
        Assert.Equal(TimeSpan.FromSeconds(41), options.PendingMessageIdleTime);
    }

    [Fact]
    public void Bind_StillBindsBackendSpecificRedisSettings()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Queue:Redis:ConnectionString"] = "queue:1234",
            ["Queue:Redis:StreamName"] = "custom:stream",
        });

        Assert.Equal("queue:1234", options.Redis.ConnectionString);
        Assert.Equal("custom:stream", options.Redis.StreamName);
    }

    [Fact]
    public void Bind_WhenOnlyLegacyHeartbeatIsSet_MapsToGenericHeartbeatAndWarns()
    {
        var logger = new RecordingLogger();

        var options = WorkQueueOptionsLoader.Bind(
            Configuration(new Dictionary<string, string?>
            {
                ["Queue:Redis:HeartbeatInterval"] = "00:00:12",
            }),
            logger);

        Assert.Equal(TimeSpan.FromSeconds(12), options.HeartbeatInterval);
        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Warning &&
            entry.Message.Contains("Queue:Redis:HeartbeatInterval", StringComparison.Ordinal));
    }

    [Fact]
    public void Bind_WhenOnlyLegacyReclaimerIsSet_MapsToGenericReclaimerAndWarns()
    {
        var logger = new RecordingLogger();

        var options = WorkQueueOptionsLoader.Bind(
            Configuration(new Dictionary<string, string?>
            {
                ["Queue:Redis:ReclaimerInterval"] = "00:00:13",
            }),
            logger);

        Assert.Equal(TimeSpan.FromSeconds(13), options.ReclaimerInterval);
        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Warning &&
            entry.Message.Contains("Queue:Redis:ReclaimerInterval", StringComparison.Ordinal));
    }

    [Fact]
    public void Bind_WhenOnlyLegacyPendingIdleTimeIsSet_MapsToGenericPendingIdleTimeAndWarns()
    {
        var logger = new RecordingLogger();

        var options = WorkQueueOptionsLoader.Bind(
            Configuration(new Dictionary<string, string?>
            {
                ["Queue:Redis:PendingMessageIdleTime"] = "00:00:45",
            }),
            logger);

        Assert.Equal(TimeSpan.FromSeconds(45), options.PendingMessageIdleTime);
        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Warning &&
            entry.Message.Contains("Queue:Redis:PendingMessageIdleTime", StringComparison.Ordinal));
    }

    [Fact]
    public void Bind_WhenGenericAndLegacyAreSet_GenericTakesPrecedenceWithoutWarning()
    {
        var logger = new RecordingLogger();

        var options = WorkQueueOptionsLoader.Bind(
            Configuration(new Dictionary<string, string?>
            {
                ["Queue:HeartbeatInterval"] = "00:00:07",
                ["Queue:Redis:HeartbeatInterval"] = "00:00:12",
            }),
            logger);

        Assert.Equal(TimeSpan.FromSeconds(7), options.HeartbeatInterval);
        Assert.DoesNotContain(logger.Entries, entry =>
            entry.Message.Contains("Queue:Redis:HeartbeatInterval", StringComparison.Ordinal));
    }

    [Fact]
    public void Bind_WhenAllLegacyKeysAreSet_LogsOneWarningPerLegacyKey()
    {
        var logger = new RecordingLogger();

        var options = WorkQueueOptionsLoader.Bind(
            Configuration(new Dictionary<string, string?>
            {
                ["Queue:Redis:HeartbeatInterval"] = "00:00:12",
                ["Queue:Redis:ReclaimerInterval"] = "00:00:13",
                ["Queue:Redis:PendingMessageIdleTime"] = "00:00:45",
            }),
            logger);

        Assert.Equal(TimeSpan.FromSeconds(12), options.HeartbeatInterval);
        Assert.Equal(TimeSpan.FromSeconds(13), options.ReclaimerInterval);
        Assert.Equal(TimeSpan.FromSeconds(45), options.PendingMessageIdleTime);

        var warnings = logger.Entries
            .Where(entry => entry.Level == LogLevel.Warning)
            .Select(entry => entry.Message)
            .ToList();

        Assert.Equal(3, warnings.Count);
        Assert.Contains(warnings, message => message.Contains("Queue:Redis:HeartbeatInterval", StringComparison.Ordinal));
        Assert.Contains(warnings, message => message.Contains("Queue:Redis:ReclaimerInterval", StringComparison.Ordinal));
        Assert.Contains(warnings, message => message.Contains("Queue:Redis:PendingMessageIdleTime", StringComparison.Ordinal));
    }

    private static WorkQueueOptions Bind(Dictionary<string, string?>? values = null) =>
        WorkQueueOptionsLoader.Bind(Configuration(values ?? []), NullLogger.Instance);

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
