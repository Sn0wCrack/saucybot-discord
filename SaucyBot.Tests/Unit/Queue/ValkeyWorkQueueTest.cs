using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SaucyBot.Diagnostics;
using SaucyBot.Queue;
using Xunit;

namespace SaucyBot.Tests.Unit.Queue;

public sealed class ValkeyWorkQueueTest
{
    [Fact]
    public async Task EnqueueRetriesTransientBackpressureAndEventuallySucceeds()
    {
        var client = new FakeValkeyStreamClient
        {
            AddFailures = 2
        };
        var queue = new ValkeyWorkQueue(client, new WorkQueueOptions { RetryDelay = TimeSpan.Zero });
        var item = CreateItem();

        await queue.EnqueueAsync(item, CancellationToken.None);

        Assert.Equal(3, client.AddCalls);
        Assert.Equal(item.Serialize(), client.Payloads.Single());
    }

    [Fact]
    public async Task ReadReturnsStreamEntryAndAcknowledgementIsExplicit()
    {
        var client = new FakeValkeyStreamClient();
        var item = CreateItem();
        client.Entries.Enqueue(new ValkeyStreamEntry("42-0", item.Serialize()));
        var queue = new ValkeyWorkQueue(client, new WorkQueueOptions { RetryDelay = TimeSpan.Zero });

        await using var messages = queue.ReadAsync("worker-1", TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await messages.MoveNextAsync());
        var queued = messages.Current;

        Assert.Equal("42-0", queued.EntryId);
        Assert.Equal(item.MessageId, queued.Item.MessageId);
        Assert.Equal(item.CorrelationId, queued.Item.CorrelationId);
        Assert.Empty(client.Acknowledged);

        await queue.AcknowledgeAsync(queued, CancellationToken.None);

        Assert.Equal(["42-0"], client.Acknowledged);
        Assert.Equal(["42-0"], client.Deleted);
        Assert.Equal(["ack:42-0", "delete:42-0"], client.Operations);
    }

    [Fact]
    public async Task ReadDoesNotReclaimPendingEntriesDuringRuntime()
    {
        var client = new FakeValkeyStreamClient();
        var item = CreateItem();
        client.Entries.Enqueue(new ValkeyStreamEntry("8-0", item.Serialize()));
        var queue = new ValkeyWorkQueue(client, new WorkQueueOptions { RetryDelay = TimeSpan.Zero });

        await using var messages = queue.ReadAsync("worker-1", TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await messages.MoveNextAsync());
        Assert.Equal("8-0", messages.Current.EntryId);
        Assert.Equal(1, client.NewReads);
    }

    [Fact]
    public async Task EnqueueCancellationStopsBackpressureRetry()
    {
        var client = new FakeValkeyStreamClient { AddFailures = int.MaxValue };
        var queue = new ValkeyWorkQueue(client, new WorkQueueOptions { RetryDelay = TimeSpan.FromSeconds(10) });
        using var cancellation = new CancellationTokenSource();

        var enqueue = queue.EnqueueAsync(CreateItem(), cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => enqueue);
    }

    [Fact]
    public async Task AcknowledgementFailureIsPropagated()
    {
        var client = new FakeValkeyStreamClient { AcknowledgeException = new InvalidOperationException("ack failed") };
        var queue = new ValkeyWorkQueue(client, new WorkQueueOptions());
        var queued = new QueuedMessageWorkItem("7-0", CreateItem());

        await Assert.ThrowsAsync<InvalidOperationException>(() => queue.AcknowledgeAsync(queued, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteFailureAfterAcknowledgementIsPropagated()
    {
        var client = new FakeValkeyStreamClient { DeleteException = new InvalidOperationException("delete failed") };
        var queue = new ValkeyWorkQueue(client, new WorkQueueOptions());
        var queued = new QueuedMessageWorkItem("7-0", CreateItem());

        await Assert.ThrowsAsync<InvalidOperationException>(() => queue.AcknowledgeAsync(queued, CancellationToken.None));

        Assert.Equal(["7-0"], client.Acknowledged);
        Assert.Equal(["7-0"], client.DeleteAttempts);
    }

    [Fact]
    public async Task MalformedEntryIsAcknowledgedDeletedAndDoesNotStopReading()
    {
        var client = new FakeValkeyStreamClient();
        client.Entries.Enqueue(new ValkeyStreamEntry("bad-0", "{\"version\":999,\"item\":null}"));
        var valid = CreateItem();
        client.Entries.Enqueue(new ValkeyStreamEntry("good-0", valid.Serialize()));
        var queue = new ValkeyWorkQueue(client, new WorkQueueOptions { RetryDelay = TimeSpan.Zero });

        await using var messages = queue.ReadAsync("worker-1", TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await messages.MoveNextAsync());
        Assert.Equal("good-0", messages.Current.EntryId);
        Assert.Equal(["bad-0"], client.Acknowledged);
        Assert.Equal(["bad-0"], client.Deleted);
    }

    [Fact]
    public async Task MalformedCleanupRetriesAcknowledgementAndDeletionInOrder()
    {
        var client = new FakeValkeyStreamClient
        {
            AcknowledgeFailures = 1,
            DeleteFailures = 1
        };
        client.Entries.Enqueue(new ValkeyStreamEntry("bad-0", "invalid"));
        client.Entries.Enqueue(new ValkeyStreamEntry("good-0", CreateItem().Serialize()));
        var queue = new ValkeyWorkQueue(client, new WorkQueueOptions { RetryDelay = TimeSpan.Zero });

        await using var messages = queue.ReadAsync("worker-1", TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await messages.MoveNextAsync());
        Assert.Equal("good-0", messages.Current.EntryId);
        Assert.Equal(["ack:bad-0", "ack:bad-0", "delete:bad-0", "delete:bad-0"], client.Operations);
    }

    [Fact]
    public async Task MalformedEntryDecrementsQueueDepthExactlyOnce()
    {
        var client = new FakeValkeyStreamClient();
        client.Entries.Enqueue(new ValkeyStreamEntry("bad-0", "invalid"));
        client.Entries.Enqueue(new ValkeyStreamEntry("good-0", CreateItem().Serialize()));
        using var metrics = new SaucyBotMetrics();
        long queueDepthDelta = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Name == "saucybot.queue.depth")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, _, _) =>
        {
            if (instrument.Name == "saucybot.queue.depth")
            {
                queueDepthDelta += measurement;
            }
        });
        listener.Start();
        var queue = new ValkeyWorkQueue(client, new WorkQueueOptions { RetryDelay = TimeSpan.Zero }, metrics);

        await using var messages = queue.ReadAsync("worker-1", TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await messages.MoveNextAsync());
        Assert.Equal("good-0", messages.Current.EntryId);

        Assert.Equal(-1, queueDepthDelta);
    }

    [Fact]
    public async Task ReadCancellationStopsWaitingForNewEntries()
    {
        var client = new FakeValkeyStreamClient();
        var queue = new ValkeyWorkQueue(client, new WorkQueueOptions());
        using var cancellation = new CancellationTokenSource();
        await using var messages = queue.ReadAsync("worker-1", cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);

        var read = messages.MoveNextAsync().AsTask();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
    }

    [Fact]
    public async Task StartupClearDelegatesOnlyWhenConfigured()
    {
        var client = new FakeValkeyStreamClient();
        var queue = new ValkeyWorkQueue(client, new WorkQueueOptions { ClearPendingOnStartup = true });

        await queue.ClearPendingAsync(CancellationToken.None);

        Assert.Equal(1, client.ClearCalls);
    }

    private static MessageWorkItem CreateItem() => new(
        1, 2, 3, 4, [5], "message", null, [], true, true, Guid.NewGuid());

    private sealed class FakeValkeyStreamClient : IValkeyStreamClient
    {
        public int AddFailures { get; set; }
        public int AddCalls { get; private set; }
        public int ClearCalls { get; private set; }
        public int NewReads { get; private set; }
        public Queue<ValkeyStreamEntry> Entries { get; } = new();
        public List<string> Payloads { get; } = [];
        public List<string> Acknowledged { get; } = [];
        public List<string> Deleted { get; } = [];
        public Exception? AcknowledgeException { get; set; }
        public Exception? DeleteException { get; set; }
        public int AcknowledgeFailures { get; set; }
        public int DeleteFailures { get; set; }
        public List<string> DeleteAttempts { get; } = [];
        public List<string> Operations { get; } = [];

        public Task EnsureGroupAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<string> AddAsync(string payload, CancellationToken cancellationToken)
        {
            AddCalls++;
            if (AddFailures-- > 0)
            {
                throw new ValkeyBackpressureException("queue full");
            }

            Payloads.Add(payload);
            return Task.FromResult("1-0");
        }

        public async Task<ValkeyStreamEntry?> ReadNewAsync(string consumer, CancellationToken cancellationToken)
        {
            NewReads++;
            while (Entries.Count == 0)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return Entries.Dequeue();
        }

        public Task AcknowledgeAsync(string entryId, CancellationToken cancellationToken)
        {
            Operations.Add($"ack:{entryId}");
            if (AcknowledgeFailures-- > 0)
            {
                return Task.FromException(new InvalidOperationException("transient ack failure"));
            }

            if (AcknowledgeException is not null)
            {
                return Task.FromException(AcknowledgeException);
            }

            Acknowledged.Add(entryId);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string entryId, CancellationToken cancellationToken)
        {
            Operations.Add($"delete:{entryId}");
            DeleteAttempts.Add(entryId);
            if (DeleteFailures-- > 0)
            {
                return Task.FromException(new InvalidOperationException("transient delete failure"));
            }

            if (DeleteException is not null)
            {
                return Task.FromException(DeleteException);
            }

            Deleted.Add(entryId);
            return Task.CompletedTask;
        }

        public Task ClearPendingAsync(CancellationToken cancellationToken)
        {
            ClearCalls++;
            return Task.CompletedTask;
        }
    }
}
