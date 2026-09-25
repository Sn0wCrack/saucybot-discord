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
using SaucyBot.Queue.Redis;
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
            var configuration = ConfigurationOptions.Parse(container.GetConnectionString());
            configuration.AllowAdmin = true;
            _connection = await ConnectionMultiplexer.ConnectAsync(configuration);
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
        var redis = CreateRedisOptions("ack");
        var client = CreateClient(redis);
        var queue = new RedisWorkQueue(client, CreateOptions(), redis);
        var item = TestItem();

        await client.EnsureGroupAsync(TestContext.Current.CancellationToken);
        var groups = await Database.StreamGroupInfoAsync(redis.StreamName);
        Assert.Contains(groups, group => group.Name == redis.ConsumerGroup);

        await queue.EnqueueAsync(item, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await using var messages = queue.ReadAsync("consumer-1", TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await messages.MoveNextAsync());

        var queued = messages.Current;
        Assert.Equal(item.MessageId, queued.Item.MessageId);
        Assert.Equal(item.GuildId, queued.Item.GuildId);
        Assert.Equal(item.ChannelId, queued.Item.ChannelId);
        Assert.Equal(item.AuthorId, queued.Item.AuthorId);
        Assert.Equal(item.AuthorRoleIds, queued.Item.AuthorRoleIds);
        Assert.Equal(item.Content, queued.Item.Content);
        Assert.Equal(item.Embeds, queued.Item.Embeds);
        Assert.Equal(item.CorrelationId, queued.Item.CorrelationId);
        await queued.Lease.CompleteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, await Database.StreamLengthAsync(redis.StreamName));
    }

    [Fact]
    public async Task MalformedEntryIsAcknowledgedDeletedAndValidEntryIsReturned()
    {
        var redis = CreateRedisOptions("malformed");
        var client = CreateClient(redis);
        var queue = new RedisWorkQueue(client, CreateOptions(), redis);

        await client.EnsureGroupAsync(TestContext.Current.CancellationToken);
        await Database.StreamAddAsync(redis.StreamName, "payload", "not-json");
        await queue.EnqueueAsync(TestItem(), TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await using var messages = queue.ReadAsync("consumer-1", TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await messages.MoveNextAsync());
        Assert.Equal(TestItem().MessageId, messages.Current.Item.MessageId);

        var entries = await Database.StreamRangeAsync(redis.StreamName);
        Assert.Single(entries);
        Assert.DoesNotContain(entries, entry => entry.Values.Any(value => value.Value == "not-json"));
    }

    [Fact]
    public async Task StartupCleanupDeletesPendingStream()
    {
        var redis = CreateRedisOptions("startup-cleanup");
        var client = CreateClient(redis);
        var queue = new RedisWorkQueue(client, CreateOptions(clearPendingOnStartup: true), redis);

        await queue.EnqueueAsync(TestItem(), TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(await Database.KeyExistsAsync(redis.StreamName));

        await queue.StartAsync(TestContext.Current.CancellationToken);

        Assert.False(await Database.KeyExistsAsync(redis.StreamName));
    }

    [Fact]
    public async Task ReadCancellationStopsWaitingForNewEntries()
    {
        var redis = CreateRedisOptions("cancellation");
        var queue = new RedisWorkQueue(CreateClient(redis), CreateOptions(), redis);
        using var cancellation = new CancellationTokenSource();
        await using var messages = queue.ReadAsync("consumer-1", cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);

        var read = messages.MoveNextAsync().AsTask();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
    }

    [Fact]
    public async Task ExplicitRecoveryReclaimsIdlePendingEntryAndReportsDeliveryCount()
    {
        var redis = CreateRedisOptions("reclaim");
        var client = CreateClient(redis);

        await client.EnsureGroupAsync(TestContext.Current.CancellationToken);
        await client.AddAsync(
            TestItem().Serialize(),
            TestContext.Current.CancellationToken);
        var first = await client.ReadNewAsync(
            "consumer-1",
            "first-lease-token",
            TestContext.Current.CancellationToken);

        Assert.NotNull(first);
        Assert.Equal(1, first.DeliveryCount);

        var recovered = await client.ReclaimAsync(
            "consumer-2",
            TimeSpan.Zero,
            "second-lease-token",
            TestContext.Current.CancellationToken);

        Assert.NotNull(recovered);
        Assert.Equal(first.EntryId, recovered.EntryId);
        Assert.Equal(2, recovered.DeliveryCount);
    }

    [Fact]
    public async Task ProducerConsumerRoundTripCompletesThroughTheLease()
    {
        var redis = CreateRedisOptions("contracts");
        var client = CreateClient(redis);
        var queue = new RedisWorkQueue(client, CreateOptions(), redis);
        IWorkItemProducer<MessageWorkItem> producer = queue;
        IWorkItemConsumer<MessageWorkItem> consumer = queue;

        await consumer.StartAsync(TestContext.Current.CancellationToken);
        var enqueued = await producer.EnqueueAsync(
            TestItem(),
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        Assert.Equal(EnqueueResult.Accepted, enqueued);

        await using var deliveries = consumer.ReadAsync("consumer-1", TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await deliveries.MoveNextAsync());
        var delivery = deliveries.Current;
        await using var lease = delivery.Lease;

        Assert.Equal(TestItem().MessageId, delivery.Item.MessageId);
        Assert.Equal(1, delivery.Attempt);
        Assert.False(lease.LostToken.IsCancellationRequested);
        Assert.Equal(
            LeaseOperationResult.Applied,
            await lease.CompleteAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await Database.StreamLengthAsync(redis.StreamName));
    }

    [Fact]
    public async Task StaleHeartbeatCannotTakeOwnershipBackAfterAnotherConsumerReclaimsIt()
    {
        var redis = CreateRedisOptions("stale-heartbeat");
        var options = CreateOptions(heartbeatInterval: TimeSpan.FromMilliseconds(30));
        var queue = new RedisWorkQueue(CreateClient(redis), options, redis);
        IWorkItemConsumer<MessageWorkItem> consumer = queue;
        await consumer.StartAsync(TestContext.Current.CancellationToken);
        await queue.EnqueueAsync(TestItem(), TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await using var firstRead = consumer.ReadAsync("consumer-a", TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await firstRead.MoveNextAsync());
        var stale = firstRead.Current;

        await using var recovery = consumer.RecoverAsync(
                "consumer-b",
                TimeSpan.Zero,
                count: 1,
                TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await recovery.MoveNextAsync());
        var current = recovery.Current;
        await using var currentLease = current.Lease;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Task.Delay(Timeout.InfiniteTimeSpan, stale.Lease.LostToken)
                .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        Assert.Equal(1, await Database.StreamLengthAsync(redis.StreamName));
        Assert.Equal(
            LeaseOperationResult.Applied,
            await currentLease.CompleteAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StaleDeliveryCannotRetryCompleteOrDeleteAfterAnotherConsumerReclaimsIt()
    {
        var redis = CreateRedisOptions("stale-owner");
        var client = CreateClient(redis);
        var queue = new RedisWorkQueue(client, CreateOptions(), redis);
        IWorkItemConsumer<MessageWorkItem> consumer = queue;
        await consumer.StartAsync(TestContext.Current.CancellationToken);
        await queue.EnqueueAsync(TestItem(), TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await using var firstRead = consumer.ReadAsync("consumer-a", TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await firstRead.MoveNextAsync());
        var stale = firstRead.Current;
        await stale.Lease.DisposeAsync();

        await using var recovery = consumer.RecoverAsync(
                "consumer-b",
                TimeSpan.Zero,
                count: 1,
                TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await recovery.MoveNextAsync());
        var current = recovery.Current;
        await using var currentLease = current.Lease;

        Assert.Equal(
            LeaseOperationResult.LeaseLost,
            await stale.Lease.RetryAsync(new InvalidOperationException("late failure"), TestContext.Current.CancellationToken));
        Assert.Equal(
            LeaseOperationResult.LeaseLost,
            await stale.Lease.CompleteAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await Database.StreamLengthAsync(redis.StreamName));
        Assert.Single(await Database.StreamPendingMessagesAsync(
            redis.StreamName,
            redis.ConsumerGroup,
            10,
            default,
            default,
            default));

        Assert.Equal(
            LeaseOperationResult.Applied,
            await currentLease.CompleteAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await Database.StreamLengthAsync(redis.StreamName));
    }

    [Fact]
    public async Task CompletionWithoutPendingRecordLeavesExistingEntryAndReturnsLeaseLost()
    {
        var redis = CreateRedisOptions("completion-without-pending");
        var queue = new RedisWorkQueue(CreateClient(redis), CreateOptions(), redis);
        IWorkItemConsumer<MessageWorkItem> consumer = queue;
        await consumer.StartAsync(TestContext.Current.CancellationToken);
        await queue.EnqueueAsync(TestItem(), TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await using var read = consumer.ReadAsync("consumer-a", TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await read.MoveNextAsync());
        await using var lease = read.Current.Lease;
        Assert.Equal(
            1,
            await Database.StreamAcknowledgeAsync(redis.StreamName, redis.ConsumerGroup, read.Current.DeliveryId));

        Assert.Equal(
            LeaseOperationResult.LeaseLost,
            await lease.CompleteAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await Database.StreamLengthAsync(redis.StreamName));
    }

    [Fact]
    public async Task RetryWithoutPendingRecordReturnsLeaseLost()
    {
        var redis = CreateRedisOptions("retry-without-pending");
        var queue = new RedisWorkQueue(CreateClient(redis), CreateOptions(), redis);
        IWorkItemConsumer<MessageWorkItem> consumer = queue;
        await consumer.StartAsync(TestContext.Current.CancellationToken);
        await queue.EnqueueAsync(TestItem(), TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await using var read = consumer.ReadAsync("consumer-a", TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await read.MoveNextAsync());
        await using var lease = read.Current.Lease;
        Assert.Equal(
            1,
            await Database.StreamAcknowledgeAsync(redis.StreamName, redis.ConsumerGroup, read.Current.DeliveryId));

        Assert.Equal(
            LeaseOperationResult.LeaseLost,
            await lease.RetryAsync(new InvalidOperationException("missing pending record"), TestContext.Current.CancellationToken));
        Assert.Equal(1, await Database.StreamLengthAsync(redis.StreamName));
    }

    [Fact]
    public async Task RecoveryAdvancesPastEmptyNonterminalAutoClaimPages()
    {
        var redis = CreateRedisOptions("recovery-cursor-empty-page");
        var client = CreateClient(redis);
        var queue = new RedisWorkQueue(client, CreateOptions(), redis);
        IWorkItemConsumer<MessageWorkItem> consumer = queue;
        await consumer.StartAsync(TestContext.Current.CancellationToken);

        for (var index = 0; index < 25; index++)
        {
            await queue.EnqueueAsync(TestItem(), TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }

        // EnqueueAsync returns only admission status. Read the entries into the PEL
        // to create a first scan page of fresh items followed by idle items.
        var entryIds = new List<string>();
        for (var index = 0; index < 25; index++)
        {
            var entry = await client.ReadNewAsync("seed", $"seed-{index}", TestContext.Current.CancellationToken);
            Assert.NotNull(entry);
            entryIds.Add(entry.EntryId);
        }

        var recent = entryIds.Take(12).ToArray();
        var idle = entryIds.Skip(12).ToArray();
        await SetPendingIdleAsync(redis, "seed-recent", recent, idleMilliseconds: 0);
        await SetPendingIdleAsync(redis, "seed-idle", idle, idleMilliseconds: 60_000);

        var recovered = new List<WorkDelivery<MessageWorkItem>>();
        for (var index = 0; index < idle.Length; index++)
        {
            await using var page = consumer.RecoverAsync(
                    "recovery-cursor",
                    TimeSpan.FromSeconds(10),
                    count: 1,
                    TestContext.Current.CancellationToken)
                .GetAsyncEnumerator(TestContext.Current.CancellationToken);
            Assert.True(await page.MoveNextAsync(), $"Recovery stopped before idle entry {index}.");
            recovered.Add(page.Current);
        }

        Assert.Equal(idle.OrderBy(id => id), recovered.Select(delivery => delivery.DeliveryId).OrderBy(id => id));

        foreach (var delivery in recovered)
        {
            Assert.Equal(
                LeaseOperationResult.Applied,
                await delivery.Lease.CompleteAsync(TestContext.Current.CancellationToken));
            await delivery.Lease.DisposeAsync();
        }

        var remainingPending = await Database.StreamPendingMessagesAsync(
            redis.StreamName,
            redis.ConsumerGroup,
            100,
            default,
            default,
            default);
        Assert.Equal(recent.Length, remainingPending.Length);
        Assert.Contains(
            await Database.StreamConsumerInfoAsync(redis.StreamName, redis.ConsumerGroup),
            consumer => consumer.Name == "seed-recent" && consumer.PendingMessageCount == recent.Length);
    }

    [Fact]
    public async Task CompletionAndOwnershipTransferRemoveZeroPendingConsumers()
    {
        const int deliveryCount = 8;
        var redis = CreateRedisOptions("consumer-cleanup");
        var queue = new RedisWorkQueue(CreateClient(redis), CreateOptions(), redis);
        IWorkItemConsumer<MessageWorkItem> consumer = queue;
        await consumer.StartAsync(TestContext.Current.CancellationToken);
        for (var index = 0; index < deliveryCount; index++)
        {
            await queue.EnqueueAsync(TestItem(), TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }

        var firstDeliveries = await ReadDeliveriesAsync(consumer, "first-owner", deliveryCount);
        foreach (var delivery in firstDeliveries.Take(deliveryCount / 2))
        {
            Assert.Equal(
                LeaseOperationResult.Applied,
                await delivery.Lease.CompleteAsync(TestContext.Current.CancellationToken));
            await delivery.Lease.DisposeAsync();
        }

        foreach (var delivery in firstDeliveries.Skip(deliveryCount / 2))
        {
            Assert.Equal(
                LeaseOperationResult.Applied,
                await delivery.Lease.RetryAsync(new InvalidOperationException("retry for recovery"), TestContext.Current.CancellationToken));
        }

        var reclaimed = await ReadDeliveriesAsync(
            consumer,
            "recovery-owner",
            deliveryCount / 2,
            minimumIdleTime: TimeSpan.Zero);
        Assert.Equal(deliveryCount / 2, reclaimed.Count);

        foreach (var delivery in reclaimed)
        {
            Assert.Equal(
                LeaseOperationResult.Applied,
                await delivery.Lease.CompleteAsync(TestContext.Current.CancellationToken));
            await delivery.Lease.DisposeAsync();
        }

        Assert.Empty(await Database.StreamConsumerInfoAsync(redis.StreamName, redis.ConsumerGroup));
    }

    [Fact]
    public async Task CompletionRemovesPerDeliveryConsumerRecords()
    {
        const int deliveryCount = 8;
        var redis = CreateRedisOptions("completed-consumer-cleanup");
        var queue = new RedisWorkQueue(CreateClient(redis), CreateOptions(), redis);
        IWorkItemConsumer<MessageWorkItem> consumer = queue;
        await consumer.StartAsync(TestContext.Current.CancellationToken);
        for (var index = 0; index < deliveryCount; index++)
        {
            await queue.EnqueueAsync(TestItem(), TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }

        var deliveries = await ReadDeliveriesAsync(consumer, "batch-owner", deliveryCount);
        foreach (var delivery in deliveries)
        {
            Assert.Equal(
                LeaseOperationResult.Applied,
                await delivery.Lease.CompleteAsync(TestContext.Current.CancellationToken));
            await delivery.Lease.DisposeAsync();
        }

        Assert.Empty(await Database.StreamConsumerInfoAsync(redis.StreamName, redis.ConsumerGroup));
    }

    [Fact]
    public async Task AlreadyAppliedCompletionDoesNotDeleteConsumerWithOtherPendingWork()
    {
        var redis = CreateRedisOptions("keep-pending-consumer");
        var client = CreateClient(redis);
        await client.EnsureGroupAsync(TestContext.Current.CancellationToken);
        await client.AddAsync(TestItem().Serialize(), TestContext.Current.CancellationToken);
        await client.AddAsync(TestItem().Serialize(), TestContext.Current.CancellationToken);

        var first = await client.ReadNewAsync("shared-owner", "shared-token", TestContext.Current.CancellationToken);
        var second = await client.ReadNewAsync("shared-owner", "shared-token", TestContext.Current.CancellationToken);
        Assert.NotNull(first);
        Assert.NotNull(second);

        Assert.Equal(
            LeaseOperationResult.Applied,
            await client.CompleteAsync("shared-owner", first.EntryId, "shared-token", TestContext.Current.CancellationToken));
        Assert.Equal(
            LeaseOperationResult.AlreadyApplied,
            await client.CompleteAsync("shared-owner", first.EntryId, "shared-token", TestContext.Current.CancellationToken));

        var consumers = await Database.StreamConsumerInfoAsync(redis.StreamName, redis.ConsumerGroup);
        var remainingOwner = Assert.Single(consumers);
        Assert.Equal("shared-owner|shared-token", remainingOwner.Name);
        Assert.Equal(1, remainingOwner.PendingMessageCount);
        var pending = await Database.StreamPendingMessagesAsync(
            redis.StreamName,
            redis.ConsumerGroup,
            10,
            default,
            default,
            default);
        Assert.Single(pending);
    }

    [Fact]
    public async Task RepeatedCompletionAfterAmbiguousResultIsAlreadyAppliedAndDoesNotRedeliver()
    {
        var redis = CreateRedisOptions("ambiguous-completion");
        var queue = new RedisWorkQueue(CreateClient(redis), CreateOptions(), redis);
        IWorkItemConsumer<MessageWorkItem> consumer = queue;
        await consumer.StartAsync(TestContext.Current.CancellationToken);
        await queue.EnqueueAsync(TestItem(), TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await using var read = consumer.ReadAsync("consumer-a", TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await read.MoveNextAsync());
        await using var lease = read.Current.Lease;

        Assert.Equal(
            LeaseOperationResult.Applied,
            await lease.CompleteAsync(TestContext.Current.CancellationToken));
        // Model a lost response by retrying the exact same mutation with the same lease.
        Assert.Equal(
            LeaseOperationResult.AlreadyApplied,
            await lease.CompleteAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await Database.StreamLengthAsync(redis.StreamName));
        Assert.Empty(await Database.StreamPendingMessagesAsync(
            redis.StreamName,
            redis.ConsumerGroup,
            10,
            default,
            default,
            default));
    }

    [Fact]
    public async Task HeartbeatKeepsDeliveryClaimedWhileItWaitsInLocalHandoff()
    {
        var redis = CreateRedisOptions("handoff-heartbeat");
        var options = CreateOptions(
            heartbeatInterval: TimeSpan.FromMilliseconds(20),
            pendingMessageIdleTime: TimeSpan.FromMilliseconds(120));
        var queue = new RedisWorkQueue(CreateClient(redis), options, redis);
        IWorkItemConsumer<MessageWorkItem> consumer = queue;
        await consumer.StartAsync(TestContext.Current.CancellationToken);
        await queue.EnqueueAsync(TestItem(), TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await using var read = consumer.ReadAsync("consumer-a", TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await read.MoveNextAsync());
        await using var lease = read.Current.Lease;

        await Task.Delay(TimeSpan.FromMilliseconds(350), TestContext.Current.CancellationToken);

        await using var recovery = consumer.RecoverAsync(
                "consumer-b",
                options.PendingMessageIdleTime,
                count: 1,
                TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.False(await recovery.MoveNextAsync());
        Assert.False(lease.LostToken.IsCancellationRequested);
        Assert.Equal(1, await Database.StreamLengthAsync(redis.StreamName));
    }

    [Fact]
    public async Task HostedWorkerProcessesAndAcknowledgesAQueuedMessage()
    {
        var redis = CreateRedisOptions("worker");
        var client = CreateClient(redis);
        var queue = new RedisWorkQueue(client, CreateOptions(), redis);
        var processor = new RecordingProcessor();
        var metrics = new SaucyBotMetrics();
        var options = new WorkQueueOptions
        {
            MessageWorkerCount = 1,
            InteractionWorkerCount = 0,
            ShutdownDrainTimeout = TimeSpan.FromSeconds(5),
        };
        await using var service = CreateHostedService(queue, processor, options, metrics);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await queue.EnqueueAsync(TestItem(), TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await processor.Processed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, processor.ProcessedCount);
        Assert.Equal(TestItem().MessageId, processor.Item!.MessageId);
        Assert.Equal(0, await Database.StreamLengthAsync(redis.StreamName));
    }

    [Fact]
    public async Task RecoveryWorkerProcessesAnIdlePendingMessage()
    {
        var redis = CreateRedisOptions("recovery-worker");
        var client = CreateClient(redis);
        var queue = new RedisWorkQueue(client, CreateOptions(), redis);
        var processor = new RecordingProcessor();
        var metrics = new SaucyBotMetrics();
        await client.EnsureGroupAsync(TestContext.Current.CancellationToken);
        await queue.EnqueueAsync(TestItem(), TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var pending = await client.ReadNewAsync("dead-worker", "dead-worker-token", TestContext.Current.CancellationToken);
        Assert.NotNull(pending);

        var options = new WorkQueueOptions
        {
            MessageWorkerCount = 1,
            InteractionWorkerCount = 0,
            PendingMessageIdleTime = TimeSpan.Zero,
            ShutdownDrainTimeout = TimeSpan.FromSeconds(5),
        };
        await using var service = CreateHostedService(queue, processor, options, metrics);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await processor.Processed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, processor.ProcessedCount);
        Assert.Equal(0, await Database.StreamLengthAsync(redis.StreamName));
    }

    [Fact]
    public async Task UsesProductionNoEvictionPolicyWithoutEvictingSmallQueueStream()
    {
        var configuration = (RedisResult[])(await Database.ExecuteAsync("CONFIG", "GET", "maxmemory-policy"))!;
        var maxMemory = (RedisResult[])(await Database.ExecuteAsync("CONFIG", "GET", "maxmemory"))!;

        Assert.Equal("noeviction", configuration[1].ToString());
        Assert.Equal("536870912", maxMemory[1].ToString());

        var redis = CreateRedisOptions("noeviction");
        var queue = new RedisWorkQueue(CreateClient(redis), CreateOptions(), redis);
        var item = TestItem();

        await queue.EnqueueAsync(item, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(await Database.KeyExistsAsync(redis.StreamName));

        await using var messages = queue.ReadAsync("consumer-1", TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await messages.MoveNextAsync());
        Assert.Equal(item.MessageId, messages.Current.Item.MessageId);

        await messages.Current.Lease.CompleteAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, await Database.StreamLengthAsync(redis.StreamName));
    }

    private ConnectionMultiplexer Connection =>
        _connection ?? throw new InvalidOperationException("The Valkey fixture did not initialize.");

    private IDatabase Database => Connection.GetDatabase();

    private static WorkQueueHostedService CreateHostedService(
        RedisWorkQueue queue,
        RecordingProcessor processor,
        WorkQueueOptions options,
        SaucyBotMetrics metrics)
    {
        var deliveries = new MessageDeliveryChannel(options);
        var interactionChannel = new InteractionWorkChannel(options);
        var interactionWorker = new InteractionQueueWorker(
            interactionChannel,
            new QueueMiddlewarePipeline<IInteractionWorkItem>([]),
            new NoOpInteractionProcessor(),
            NullLogger<InteractionQueueWorker>.Instance,
            metrics);
        return new WorkQueueHostedService(
            queue,
            deliveries,
            new MessageQueueReader(queue, deliveries, NullLogger<MessageQueueReader>.Instance),
            new MessageRecoveryWorker(queue, deliveries, options, NullLogger<MessageRecoveryWorker>.Instance, metrics),
            new MessageQueueWorker(
                deliveries,
                new QueueMiddlewarePipeline<MessageWorkItem>([]),
                processor,
                options,
                NullLogger<MessageQueueWorker>.Instance,
                metrics),
            options,
            NullLogger<WorkQueueHostedService>.Instance,
            interactionChannel,
            interactionWorker,
            metrics);
    }

    private IRedisStreamClient CreateClient(RedisWorkQueueOptions options) =>
        new StackExchangeRedisStreamClient(Connection, options, NullLogger<StackExchangeRedisStreamClient>.Instance);

    private static WorkQueueOptions CreateOptions(
        bool clearPendingOnStartup = false,
        TimeSpan? heartbeatInterval = null,
        TimeSpan? pendingMessageIdleTime = null) => new()
        {
            ClearPendingOnStartup = clearPendingOnStartup,
            HeartbeatInterval = heartbeatInterval ?? TimeSpan.FromSeconds(5),
            PendingMessageIdleTime = pendingMessageIdleTime ?? TimeSpan.FromSeconds(30),
        };

    private async Task SetPendingIdleAsync(
        RedisWorkQueueOptions redis,
        string owner,
        IReadOnlyList<string> entryIds,
        long idleMilliseconds)
    {
        var arguments = new List<object>
        {
            redis.StreamName,
            redis.ConsumerGroup,
            owner,
            0L,
        };
        arguments.AddRange(entryIds.Cast<object>());
        arguments.Add("IDLE");
        arguments.Add(idleMilliseconds);
        arguments.Add("JUSTID");
        await Database.ExecuteAsync("XCLAIM", arguments.ToArray());
    }

    private static async Task<List<WorkDelivery<MessageWorkItem>>> ReadDeliveriesAsync(
        IWorkItemConsumer<MessageWorkItem> consumer,
        string owner,
        int count,
        TimeSpan? minimumIdleTime = null)
    {
        var deliveries = new List<WorkDelivery<MessageWorkItem>>();
        await using var iterator = consumer.RecoverAsync(
                owner,
                minimumIdleTime ?? TimeSpan.Zero,
                count,
                TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        while (await iterator.MoveNextAsync())
        {
            deliveries.Add(iterator.Current);
        }

        return deliveries;
    }

    private static async Task<List<WorkDelivery<MessageWorkItem>>> ReadDeliveriesAsync(
        IWorkItemConsumer<MessageWorkItem> consumer,
        string owner,
        int count)
    {
        var deliveries = new List<WorkDelivery<MessageWorkItem>>();
        await using var iterator = consumer.ReadAsync(owner, TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        for (var index = 0; index < count; index++)
        {
            Assert.True(await iterator.MoveNextAsync());
            deliveries.Add(iterator.Current);
        }

        return deliveries;
    }

    private static RedisWorkQueueOptions CreateRedisOptions(string name) => new()
    {
        StreamName = $"integration:queue:{Interlocked.Increment(ref _streamNumber)}:{name}",
        ConsumerGroup = $"integration-workers-{name}",
        RetryDelay = TimeSpan.FromMilliseconds(10),
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
        public MessageWorkItem? Item { get; private set; }
        public int ProcessedCount { get; private set; }

        public Task ProcessAsync(MessageWorkItem item, CancellationToken cancellationToken)
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
