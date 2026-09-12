using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using Microsoft.Extensions.Logging.Abstractions;
using SaucyBot.Diagnostics;
using SaucyBot.Queue;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace SaucyBot.Tests.Integration.Redis;

[Trait("Category", "Integration")]
public sealed class RedisWorkQueueIntegrationTest : IAsyncLifetime
{
    private const string ValkeyImage =
        "docker.io/valkey/valkey:9-alpine@sha256:a174b894902bd3367e330d47cc2054367dc4917701776aaf336f41d83b65ec7a";
    private static int _streamNumber;

    private RedisContainer? _container;
    private ConnectionMultiplexer? _connection;

    public static bool IntegrationFilterSelected =>
        Environment.GetCommandLineArgs().Any(argument =>
            argument.Contains("Category=Integration", StringComparison.OrdinalIgnoreCase));

    public async ValueTask InitializeAsync()
    {
        if (!IntegrationFilterSelected)
        {
            Assert.Skip("Redis integration tests require --filter Category=Integration.");
            return;
        }

        var runtime = await ContainerRuntime.CheckAsync();
        if (!runtime.Available)
        {
            Assert.Skip(runtime.Message);
            return;
        }

        RedisContainer? container = null;
        try
        {
            container = new RedisBuilder(ValkeyImage)
                .WithCommand([
                    "valkey-server",
                    "--maxmemory",
                    "512mb",
                    "--maxmemory-policy",
                    "noeviction",
                    "--save",
                    "",
                    "--appendonly",
                    "no",
                ])
                .Build();
            await container.StartAsync();
            _connection = await ConnectionMultiplexer.ConnectAsync(container.GetConnectionString());
            _container = container;
        }
        catch (DockerUnavailableException exception)
        {
            if (container is not null)
            {
                await container.DisposeAsync();
            }

            Assert.Skip($"The {runtime.Name} runtime was found but Testcontainers could not connect to it: {exception.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.CloseAsync();
            _connection.Dispose();
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    [Fact]
    public async Task CreatesConsumerGroupEnqueuesReadsAndAcknowledgesWithDeletion()
    {
        var options = CreateOptions("ack");
        var client = CreateClient(options);
        var queue = new RedisWorkQueue(client, options);
        var item = TestItem();

        await client.EnsureGroupAsync(TestContext.Current.CancellationToken);
        var groups = await Database.StreamGroupInfoAsync(options.StreamName);
        Assert.Contains(groups, group => group.Name == options.ConsumerGroup);

        await queue.EnqueueAsync(item, TestContext.Current.CancellationToken);
        await using var messages = queue.ReadAsync("consumer-1", TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await messages.MoveNextAsync());

        var queued = messages.Current;
        Assert.Equal(item, queued.Item);
        await queue.AcknowledgeAsync(queued, TestContext.Current.CancellationToken);

        Assert.Equal(0, await Database.StreamLengthAsync(options.StreamName));
    }

    [Fact]
    public async Task MalformedEntryIsAcknowledgedDeletedAndValidEntryIsReturned()
    {
        var options = CreateOptions("malformed");
        var client = CreateClient(options);
        var queue = new RedisWorkQueue(client, options);

        await client.EnsureGroupAsync(TestContext.Current.CancellationToken);
        await Database.StreamAddAsync(options.StreamName, "payload", "not-json");
        await queue.EnqueueAsync(TestItem(), TestContext.Current.CancellationToken);

        await using var messages = queue.ReadAsync("consumer-1", TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await messages.MoveNextAsync());
        Assert.Equal(TestItem().MessageId, messages.Current.Item.MessageId);

        var entries = await Database.StreamRangeAsync(options.StreamName);
        Assert.Single(entries);
        Assert.DoesNotContain(entries, entry => entry.Values.Any(value => value.Value == "not-json"));
    }

    [Fact]
    public async Task StartupCleanupDeletesPendingStream()
    {
        var options = CreateOptions("startup-cleanup", clearPendingOnStartup: true);
        var client = CreateClient(options);
        var queue = new RedisWorkQueue(client, options);

        await queue.EnqueueAsync(TestItem(), TestContext.Current.CancellationToken);
        Assert.True(await Database.KeyExistsAsync(options.StreamName));

        await queue.ClearPendingAsync(TestContext.Current.CancellationToken);

        Assert.False(await Database.KeyExistsAsync(options.StreamName));
    }

    [Fact]
    public async Task ReadCancellationStopsWaitingForNewEntries()
    {
        var options = CreateOptions("cancellation");
        var queue = new RedisWorkQueue(CreateClient(options), options);
        using var cancellation = new CancellationTokenSource();
        await using var messages = queue.ReadAsync("consumer-1", cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);

        var read = messages.MoveNextAsync().AsTask();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
    }

    [Fact]
    public async Task HostedWorkerProcessesAndAcknowledgesAQueuedMessage()
    {
        var options = CreateOptions("worker");
        var queue = new RedisWorkQueue(CreateClient(options), options);
        var processor = new RecordingProcessor();
        await using var service = new WorkQueueHostedService(
            queue,
            processor,
            new WorkQueueOptions
            {
                StreamName = options.StreamName,
                ConsumerGroup = options.ConsumerGroup,
                RetryDelay = options.RetryDelay,
                MessageWorkerCount = 1,
                InteractionWorkerCount = 0,
                ShutdownDrainTimeout = TimeSpan.FromSeconds(5),
            },
            NullLogger<WorkQueueHostedService>.Instance,
            new InteractionWorkChannel(new WorkQueueOptions { InteractionWorkerCount = 0 }),
            new NoOpInteractionProcessor(),
            new SaucyBotMetrics());

        await service.StartAsync(TestContext.Current.CancellationToken);
        await queue.EnqueueAsync(TestItem(), TestContext.Current.CancellationToken);
        await processor.Processed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, processor.ProcessedCount);
        Assert.Equal(TestItem().MessageId, processor.Item!.Item.MessageId);
        Assert.Equal(0, await Database.StreamLengthAsync(options.StreamName));
    }

    [Fact]
    public async Task UsesProductionNoEvictionPolicyWithoutEvictingSmallQueueStream()
    {
        var configuration = Assert.IsType<RedisResult[]>(
            await Database.ExecuteAsync("CONFIG", "GET", "maxmemory-policy"));
        var maxMemory = Assert.IsType<RedisResult[]>(
            await Database.ExecuteAsync("CONFIG", "GET", "maxmemory"));

        Assert.Equal("noeviction", configuration[1].ToString());
        Assert.Equal("536870912", maxMemory[1].ToString());

        var options = CreateOptions("noeviction");
        var queue = new RedisWorkQueue(CreateClient(options), options);
        var item = TestItem();

        await queue.EnqueueAsync(item, TestContext.Current.CancellationToken);
        Assert.True(await Database.KeyExistsAsync(options.StreamName));

        await using var messages = queue.ReadAsync("consumer-1", TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await messages.MoveNextAsync());
        Assert.Equal(item.MessageId, messages.Current.Item.MessageId);

        await queue.AcknowledgeAsync(messages.Current, TestContext.Current.CancellationToken);
        Assert.Equal(0, await Database.StreamLengthAsync(options.StreamName));
    }

    private ConnectionMultiplexer Connection =>
        _connection ?? throw new InvalidOperationException("The Valkey fixture did not initialize.");

    private IDatabase Database => Connection.GetDatabase();

    private IRedisStreamClient CreateClient(WorkQueueOptions options) =>
        new StackExchangeRedisStreamClient(Connection, options, NullLogger<StackExchangeRedisStreamClient>.Instance);

    private static WorkQueueOptions CreateOptions(string name, bool clearPendingOnStartup = false) => new()
    {
        StreamName = $"integration:queue:{Interlocked.Increment(ref _streamNumber)}:{name}",
        ConsumerGroup = $"integration-workers-{name}",
        RetryDelay = TimeSpan.FromMilliseconds(10),
        ClearPendingOnStartup = clearPendingOnStartup,
        MalformedCleanupMaxAttempts = 2,
        MalformedCleanupMaxDelay = TimeSpan.FromMilliseconds(10),
    };

    private static MessageWorkItem TestItem() => new(
        1,
        2,
        3,
        4,
        [5],
        "integration-message",
        null,
        [],
        true,
        true,
        Guid.Parse("66666666-6666-6666-6666-666666666666"));

    private sealed class RecordingProcessor : IWorkItemProcessor
    {
        public TaskCompletionSource Processed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public QueuedMessageWorkItem? Item { get; private set; }
        public int ProcessedCount { get; private set; }

        public Task ProcessAsync(QueuedMessageWorkItem item, CancellationToken cancellationToken)
        {
            Item = item;
            ProcessedCount++;
            Processed.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpInteractionProcessor : IInteractionProcessor
    {
        public Task ProcessAsync(IInteractionWorkItem interaction, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Redis integration tests do not process Discord interactions.");
    }

    private sealed record RuntimeCheck(bool Available, string Name, string Message)
    {
        public static RuntimeCheck Found(string name) => new(true, name, string.Empty);
    }

    private static class ContainerRuntime
    {
        public static async Task<RuntimeCheck> CheckAsync()
        {
            var docker = await ProbeAsync("docker", ["version", "--format", "{{.Server.Version}}"]);
            if (docker.Available)
            {
                return RuntimeCheck.Found("Docker");
            }

            var podman = await ProbeAsync("podman", ["info", "--format", "{{.Version.Version}}"]);
            if (podman.Available)
            {
                return RuntimeCheck.Found("Podman");
            }

            return new RuntimeCheck(
                false,
                "none",
                "Redis integration tests skipped: neither Docker nor Podman is available. "
                + $"Docker: {docker.Message}; Podman: {podman.Message}");
        }

        private static async Task<RuntimeCheck> ProbeAsync(string command, IReadOnlyList<string> arguments)
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = command,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    },
                };
                foreach (var argument in arguments)
                {
                    process.StartInfo.ArgumentList.Add(argument);
                }

                process.Start();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(timeout.Token);
                if (process.ExitCode == 0)
                {
                    return RuntimeCheck.Found(command);
                }

                var error = await process.StandardError.ReadToEndAsync();
                return new RuntimeCheck(false, command, error.Trim());
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return new RuntimeCheck(false, command, exception.Message);
            }
            catch (OperationCanceledException)
            {
                return new RuntimeCheck(false, command, "runtime probe timed out");
            }
        }
    }
}
