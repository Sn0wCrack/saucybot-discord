using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    }

    [Fact]
    public async Task ReadReclaimsPendingEntryBeforeReadingNewEntries()
    {
        var client = new FakeValkeyStreamClient();
        var item = CreateItem();
        client.PendingEntries.Enqueue(new ValkeyStreamEntry("7-0", item.Serialize()));
        client.PendingEntries.Enqueue(new ValkeyStreamEntry("8-0", item.Serialize()));
        var queue = new ValkeyWorkQueue(client, new WorkQueueOptions { RetryDelay = TimeSpan.Zero });

        await using var messages = queue.ReadAsync("worker-1", TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await messages.MoveNextAsync());
        Assert.Equal("7-0", messages.Current.EntryId);
        Assert.True(await messages.MoveNextAsync());
        Assert.Equal("8-0", messages.Current.EntryId);
        Assert.Equal(2, client.PendingClaims);
        Assert.Equal(0, client.NewReads);
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
        public int PendingClaims { get; private set; }
        public int NewReads { get; private set; }
        public Queue<ValkeyStreamEntry> Entries { get; } = new();
        public Queue<ValkeyStreamEntry> PendingEntries { get; } = new();
        public List<string> Payloads { get; } = [];
        public List<string> Acknowledged { get; } = [];
        public Exception? AcknowledgeException { get; set; }

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

        public Task<ValkeyStreamEntry?> ClaimPendingAsync(string consumer, CancellationToken cancellationToken)
        {
            PendingClaims++;
            return Task.FromResult(PendingEntries.Count == 0 ? null : PendingEntries.Dequeue());
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
            if (AcknowledgeException is not null)
            {
                return Task.FromException(AcknowledgeException);
            }

            Acknowledged.Add(entryId);
            return Task.CompletedTask;
        }

        public Task ClearPendingAsync(CancellationToken cancellationToken)
        {
            ClearCalls++;
            return Task.CompletedTask;
        }
    }
}
