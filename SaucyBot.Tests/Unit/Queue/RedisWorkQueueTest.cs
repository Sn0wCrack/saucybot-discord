using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SaucyBot.Diagnostics;
using SaucyBot.Queue;
using SaucyBot.Queue.Redis;
using SaucyBot.Tests.Unit.Common;
using Xunit;

namespace SaucyBot.Tests.Unit.Queue;

public sealed class RedisWorkQueueTest
{
    [Fact]
    public async Task ProducerEnqueueReturnsAcceptedForDeliveredItem()
    {
        var client = new FakeRedisStreamClient();
        IWorkItemProducer<MessageWorkItem> producer = CreateQueue(client, redisOptions: new() { RetryDelay = TimeSpan.Zero });
        var item = CreateItem();

        var result = await producer.EnqueueAsync(item, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Equal(EnqueueResult.Accepted, result);
        Assert.Equal(1, client.AddCalls);
        Assert.Equal(item.Serialize(), client.Payloads.Single());
    }

    [Fact]
    public async Task ProducerEnqueueReturnsTimedOutWhileBackpressurePersists()
    {
        var client = new FakeRedisStreamClient { AddFailures = int.MaxValue };
        IWorkItemProducer<MessageWorkItem> producer = CreateQueue(
            client,
            redisOptions: new() { RetryDelay = TimeSpan.FromMilliseconds(5) });

        var result = await producer.EnqueueAsync(
            CreateItem(),
            TimeSpan.FromMilliseconds(50),
            CancellationToken.None);

        Assert.Equal(EnqueueResult.TimedOut, result);
        Assert.Empty(client.Payloads);
    }

    [Fact]
    public async Task ProducerEnqueueReturnsTimedOutWithinLimitWhenAddRemainsIncomplete()
    {
        var client = new FakeRedisStreamClient { AddHangs = true };
        IWorkItemProducer<MessageWorkItem> producer = CreateQueue(client);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await producer
                .EnqueueAsync(CreateItem(), TimeSpan.FromMilliseconds(100), CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            stopwatch.Stop();

            Assert.Equal(EnqueueResult.TimedOut, result);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"enqueue was not bounded, took {stopwatch.Elapsed}");
            Assert.Equal(1, client.AddCalls);
        }
        finally
        {
            client.AddCompletion.TrySetResult("1-0");
        }
    }

    [Fact]
    public async Task LeaseCompletionReturnsOutcomeUnknownWithinLimitWhenMutationHangs()
    {
        var client = new FakeRedisStreamClient
        {
            CompleteCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        client.Entries.Enqueue(new RedisStreamEntry("42-0", CreateItem().Serialize()));
        using var metrics = new SaucyBotMetrics();
        var timeoutCount = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (ReferenceEquals(instrument, metrics.BackendOperationTimedOut))
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, tags, _) =>
        {
            if (tags.ToArray().Any(tag => tag.Key == "operation" && Equals(tag.Value, "complete")))
            {
                timeoutCount += (int)measurement;
            }
        });
        listener.Start();
        IWorkItemConsumer<MessageWorkItem> consumer = CreateQueue(
            client,
            new WorkQueueOptions { BackendOperationTimeout = TimeSpan.FromMilliseconds(100) },
            metrics: metrics);
        var delivery = await ReadSingleDeliveryAsync(consumer, TestContext.Current.CancellationToken);
        await using var lease = delivery.Lease;

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await lease
                .CompleteAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            stopwatch.Stop();

            Assert.Equal(LeaseOperationResult.OutcomeUnknown, result);
            Assert.Equal(2, timeoutCount);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"completion was not bounded, took {stopwatch.Elapsed}");
            Assert.Empty(client.Acknowledged);
        }
        finally
        {
            client.CompleteCompletion?.TrySetResult();
        }
    }

    [Fact]
    public async Task LeaseCompletionRepeatsTheMutationWithTheSameLeaseTokenAfterOutcomeUnknown()
    {
        var client = new FakeRedisStreamClient { CompleteUnknownResults = 1 };
        client.Entries.Enqueue(new RedisStreamEntry("42-0", CreateItem().Serialize()));
        IWorkItemConsumer<MessageWorkItem> consumer = CreateQueue(
            client,
            new WorkQueueOptions { BackendOperationTimeout = TimeSpan.FromSeconds(5) });
        var delivery = await ReadSingleDeliveryAsync(consumer, TestContext.Current.CancellationToken);
        await using var lease = delivery.Lease;

        var result = await lease.CompleteAsync(CancellationToken.None);

        Assert.Equal(LeaseOperationResult.Applied, result);
        Assert.Equal(2, client.Operations.Count(operation => operation == "complete:42-0"));
        Assert.Single(client.CompleteTokens.Distinct());
        Assert.Equal(["42-0"], client.Acknowledged);
        Assert.Equal(["42-0"], client.Deleted);
    }

    [Fact]
    public async Task LeaseCompletionKeepsOutcomeUnknownWhenEveryAttemptIsUnknown()
    {
        var client = new FakeRedisStreamClient { CompleteUnknownResults = int.MaxValue };
        client.Entries.Enqueue(new RedisStreamEntry("42-0", CreateItem().Serialize()));
        IWorkItemConsumer<MessageWorkItem> consumer = CreateQueue(
            client,
            new WorkQueueOptions { BackendOperationTimeout = TimeSpan.FromSeconds(5) });
        var delivery = await ReadSingleDeliveryAsync(consumer, TestContext.Current.CancellationToken);
        await using var lease = delivery.Lease;

        var result = await lease.CompleteAsync(CancellationToken.None);

        Assert.Equal(LeaseOperationResult.OutcomeUnknown, result);
        Assert.Equal(2, client.Operations.Count(operation => operation == "complete:42-0"));
        Assert.Empty(client.Acknowledged);
    }

    [Fact]
    public async Task LeaseRetryReturnsOutcomeUnknownWithinLimitWhenMutationHangs()
    {
        var client = new FakeRedisStreamClient { RetryHangs = true };
        client.Entries.Enqueue(new RedisStreamEntry("42-0", CreateItem().Serialize(), DeliveryCount: 1));
        IWorkItemConsumer<MessageWorkItem> consumer = CreateQueue(
            client,
            new WorkQueueOptions
            {
                MaxProcessingAttempts = 3,
                BackendOperationTimeout = TimeSpan.FromMilliseconds(100),
            });
        var delivery = await ReadSingleDeliveryAsync(consumer, TestContext.Current.CancellationToken);
        await using var lease = delivery.Lease;

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await lease
                .RetryAsync(new InvalidOperationException("failed"), CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            stopwatch.Stop();

            Assert.Equal(LeaseOperationResult.OutcomeUnknown, result);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"retry was not bounded, took {stopwatch.Elapsed}");
        }
        finally
        {
            client.RetryCompletion.TrySetResult(LeaseOperationResult.Applied);
        }
    }

    [Fact]
    public async Task LeaseRetryRepeatsTheMutationWithTheSameLeaseTokenAfterOutcomeUnknown()
    {
        var client = new FakeRedisStreamClient { RetryUnknownResults = 1 };
        client.Entries.Enqueue(new RedisStreamEntry("42-0", CreateItem().Serialize(), DeliveryCount: 1));
        IWorkItemConsumer<MessageWorkItem> consumer = CreateQueue(
            client,
            new WorkQueueOptions
            {
                MaxProcessingAttempts = 3,
                BackendOperationTimeout = TimeSpan.FromSeconds(5),
            });
        var delivery = await ReadSingleDeliveryAsync(consumer, TestContext.Current.CancellationToken);
        await using var lease = delivery.Lease;

        var result = await lease.RetryAsync(new InvalidOperationException("failed"), CancellationToken.None);

        Assert.Equal(LeaseOperationResult.Applied, result);
        Assert.Equal(2, client.Operations.Count(operation => operation == "retry:42-0"));
        Assert.Single(client.RetryTokens.Distinct());
    }

    [Fact]
    public async Task LeaseRenewalStopsWithinLimitWhenRenewalHangs()
    {
        var client = new FakeRedisStreamClient { RenewHangs = true };
        client.Entries.Enqueue(new RedisStreamEntry("42-0", CreateItem().Serialize()));
        IWorkItemConsumer<MessageWorkItem> consumer = CreateQueue(
            client,
            new WorkQueueOptions
            {
                HeartbeatInterval = TimeSpan.FromMilliseconds(10),
                BackendOperationTimeout = TimeSpan.FromMilliseconds(50),
                MaxProcessingTime = TimeSpan.FromSeconds(30),
            });
        var delivery = await ReadSingleDeliveryAsync(consumer, TestContext.Current.CancellationToken);
        await using var lease = delivery.Lease;

        await client.RenewObserved.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => Task.Delay(Timeout.InfiniteTimeSpan, lease.LostToken)
                    .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));

            Assert.Equal(
                LeaseOperationResult.LeaseLost,
                await lease.CompleteAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        }
        finally
        {
            client.RenewCompletion.TrySetResult(true);
        }
    }

    [Fact]
    public async Task MalformedCleanupIsBoundedWhenCompletionHangs()
    {
        var client = new FakeRedisStreamClient
        {
            CompleteCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        client.Entries.Enqueue(new RedisStreamEntry("bad-0", "invalid"));
        client.Entries.Enqueue(new RedisStreamEntry("good-0", CreateItem().Serialize()));
        IWorkItemConsumer<MessageWorkItem> consumer = CreateQueue(
            client,
            new WorkQueueOptions { BackendOperationTimeout = TimeSpan.FromMilliseconds(100) },
            redisOptions: new()
            {
                RetryDelay = TimeSpan.Zero,
                MalformedCleanupMaxAttempts = 2,
            });

        var stopwatch = Stopwatch.StartNew();
        var messages = consumer.ReadAsync("worker-1", TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Task<bool>? pending = null;
        try
        {
            pending = messages.MoveNextAsync().AsTask();
            Assert.True(await pending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            stopwatch.Stop();

            Assert.Equal("good-0", messages.Current.DeliveryId);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"cleanup was not bounded, took {stopwatch.Elapsed}");
            Assert.Equal(2, client.Operations.Count(operation => operation == "complete:bad-0"));
        }
        finally
        {
            client.CompleteCompletion?.TrySetResult();
            if (pending is not null)
            {
                try
                {
                    await pending.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
                }
                catch (Exception)
                {
                    // Failure output above is authoritative; only drain the read here.
                }
            }

            await messages.DisposeAsync();
        }
    }

    [Fact]
    public async Task ProducerEnqueueDoesNotRetryAnAmbiguousAdd()
    {
        var client = new FakeRedisStreamClient { AddAmbiguousFailures = 1 };
        IWorkItemProducer<MessageWorkItem> producer = CreateQueue(client, redisOptions: new() { RetryDelay = TimeSpan.Zero });

        var result = await producer.EnqueueAsync(
            CreateItem(),
            TimeSpan.FromMilliseconds(200),
            CancellationToken.None);

        Assert.Equal(EnqueueResult.TimedOut, result);
        Assert.Equal(1, client.AddCalls);
    }

    [Fact]
    public async Task ConsumerReadYieldsDeliveryWithOpaqueIdAttemptAndLease()
    {
        var client = new FakeRedisStreamClient();
        var item = CreateItem();
        client.Entries.Enqueue(new RedisStreamEntry("42-0", item.Serialize(), DeliveryCount: 3));
        IWorkItemConsumer<MessageWorkItem> consumer = CreateQueue(client);

        var delivery = await ReadSingleDeliveryAsync(consumer, TestContext.Current.CancellationToken);
        await delivery.Lease.DisposeAsync();

        Assert.Equal("42-0", delivery.DeliveryId);
        Assert.Equal(3, delivery.Attempt);
        Assert.Equal(item.MessageId, delivery.Item.MessageId);
        Assert.Equal(item.CorrelationId, delivery.Item.CorrelationId);
        Assert.True(delivery.ReceivedAt > DateTimeOffset.MinValue);
        Assert.False(delivery.IsRecovered);
        Assert.False(delivery.Lease.LostToken.IsCancellationRequested);
    }

    [Fact]
    public async Task ConsumerReadDiscardsMalformedEntriesAndKeepsReading()
    {
        var client = new FakeRedisStreamClient();
        var valid = CreateItem();
        client.Entries.Enqueue(new RedisStreamEntry("bad-0", "invalid"));
        client.Entries.Enqueue(new RedisStreamEntry("good-0", valid.Serialize()));
        IWorkItemConsumer<MessageWorkItem> consumer = CreateQueue(
            client,
            redisOptions: new() { RetryDelay = TimeSpan.Zero });

        var delivery = await ReadSingleDeliveryAsync(consumer, TestContext.Current.CancellationToken);
        await delivery.Lease.DisposeAsync();

        Assert.Equal("good-0", delivery.DeliveryId);
        Assert.Equal(["complete:bad-0"], client.Operations);
    }

    [Fact]
    public async Task ConsumerRecoverYieldsDeliveryWithAttemptFromReclaimedEntry()
    {
        var client = new FakeRedisStreamClient();
        var item = CreateItem();
        client.ReclaimedEntries.Enqueue(new RedisStreamEntry("42-0", item.Serialize(), DeliveryCount: 2));
        IWorkItemConsumer<MessageWorkItem> consumer = CreateQueue(client);

        await using var deliveries = consumer
            .RecoverAsync("recovery-1", TimeSpan.Zero, 10, TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await deliveries.MoveNextAsync());
        var delivery = deliveries.Current;
        await delivery.Lease.DisposeAsync();

        Assert.Equal("42-0", delivery.DeliveryId);
        Assert.True(delivery.IsRecovered);
        Assert.Equal(2, delivery.Attempt);
        Assert.Equal(item.MessageId, delivery.Item.MessageId);
        Assert.Equal(0, client.NewReads);
    }

    [Fact]
    public async Task RecoveringMalformedEntryDoesNotDecrementQueueDepthAgain()
    {
        var client = new FakeRedisStreamClient();
        client.ReclaimedEntries.Enqueue(new RedisStreamEntry("bad-0", "invalid"));
        using var metrics = new SaucyBotMetrics();
        long queueDepthDelta = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (ReferenceEquals(instrument, metrics.QueueDepth))
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, _, _) => queueDepthDelta += measurement);
        listener.Start();
        var consumer = CreateQueue(client, metrics: metrics);
        await using var deliveries = consumer
            .RecoverAsync("recovery-1", TimeSpan.Zero, 1, TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.False(await deliveries.MoveNextAsync());
        Assert.Equal(0, queueDepthDelta);
    }

    [Fact]
    public async Task LeaseCompletionAcknowledgesAndDeletesTheDelivery()
    {
        var client = new FakeRedisStreamClient();
        client.Entries.Enqueue(new RedisStreamEntry("42-0", CreateItem().Serialize()));
        IWorkItemConsumer<MessageWorkItem> consumer = CreateQueue(client);
        var delivery = await ReadSingleDeliveryAsync(consumer, TestContext.Current.CancellationToken);
        await using var lease = delivery.Lease;
        Assert.Empty(client.Acknowledged);

        var result = await lease.CompleteAsync(CancellationToken.None);

        Assert.Equal(LeaseOperationResult.Applied, result);
        Assert.Equal(["complete:42-0"], client.Operations);
        Assert.Equal(["42-0"], client.Acknowledged);
        Assert.Equal(["42-0"], client.Deleted);
    }

    [Fact]
    public async Task LeaseRetryBelowAttemptLimitLeavesDeliveryPendingForRecovery()
    {
        var client = new FakeRedisStreamClient();
        client.Entries.Enqueue(new RedisStreamEntry("42-0", CreateItem().Serialize(), DeliveryCount: 1));
        IWorkItemConsumer<MessageWorkItem> consumer = CreateQueue(
            client,
            new WorkQueueOptions { MaxProcessingAttempts = 3 });
        var delivery = await ReadSingleDeliveryAsync(consumer, TestContext.Current.CancellationToken);
        await using var lease = delivery.Lease;

        var result = await lease.RetryAsync(new InvalidOperationException("failed"), CancellationToken.None);

        Assert.Equal(LeaseOperationResult.Applied, result);
        Assert.Equal(["retry:42-0"], client.Operations);
    }

    [Fact]
    public async Task LeaseRetryAtAttemptLimitDiscardsTheDelivery()
    {
        var client = new FakeRedisStreamClient();
        client.Entries.Enqueue(new RedisStreamEntry("42-0", CreateItem().Serialize(), DeliveryCount: 3));
        IWorkItemConsumer<MessageWorkItem> consumer = CreateQueue(
            client,
            new WorkQueueOptions { MaxProcessingAttempts = 3 });
        var delivery = await ReadSingleDeliveryAsync(consumer, TestContext.Current.CancellationToken);
        await using var lease = delivery.Lease;

        var result = await lease.RetryAsync(new InvalidOperationException("failed"), CancellationToken.None);

        Assert.Equal(LeaseOperationResult.Applied, result);
        Assert.Equal(["complete:42-0"], client.Operations);
    }

    [Fact]
    public async Task LeaseRenewsFromDeliveryCreationUntilCompletion()
    {
        var client = new FakeRedisStreamClient();
        client.Entries.Enqueue(new RedisStreamEntry("42-0", CreateItem().Serialize()));
        IWorkItemConsumer<MessageWorkItem> consumer = CreateQueue(
            client,
            new WorkQueueOptions { HeartbeatInterval = TimeSpan.FromMilliseconds(10) });
        var delivery = await ReadSingleDeliveryAsync(consumer, TestContext.Current.CancellationToken);
        await using var lease = delivery.Lease;

        await client.RenewObserved.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal("worker-1", client.LastRenewConsumer);
        Assert.Equal("42-0", client.LastRenewEntryId);

        Assert.Equal(LeaseOperationResult.Applied, await lease.CompleteAsync(CancellationToken.None));

        var renewalsAfterCompletion = client.RenewCalls;
        await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
        Assert.Equal(renewalsAfterCompletion, client.RenewCalls);
    }

    [Fact]
    public async Task LeaseDisposalStopsRenewal()
    {
        var client = new FakeRedisStreamClient();
        client.Entries.Enqueue(new RedisStreamEntry("42-0", CreateItem().Serialize()));
        IWorkItemConsumer<MessageWorkItem> consumer = CreateQueue(
            client,
            new WorkQueueOptions { HeartbeatInterval = TimeSpan.FromMilliseconds(10) });
        var delivery = await ReadSingleDeliveryAsync(consumer, TestContext.Current.CancellationToken);

        await client.RenewObserved.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await delivery.Lease.DisposeAsync();

        var renewalsAfterDisposal = client.RenewCalls;
        await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
        Assert.Equal(renewalsAfterDisposal, client.RenewCalls);
    }

    [Fact]
    public async Task LeaseLossSignalsLostTokenAndRejectsCompletionAndRetry()
    {
        var client = new FakeRedisStreamClient { RenewResult = false };
        client.Entries.Enqueue(new RedisStreamEntry("42-0", CreateItem().Serialize()));
        IWorkItemConsumer<MessageWorkItem> consumer = CreateQueue(
            client,
            new WorkQueueOptions { HeartbeatInterval = TimeSpan.FromMilliseconds(10) });
        var delivery = await ReadSingleDeliveryAsync(consumer, TestContext.Current.CancellationToken);
        await using var lease = delivery.Lease;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Task.Delay(Timeout.InfiniteTimeSpan, lease.LostToken)
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        Assert.Equal(LeaseOperationResult.LeaseLost, await lease.CompleteAsync(CancellationToken.None));
        Assert.Equal(
            LeaseOperationResult.LeaseLost,
            await lease.RetryAsync(new InvalidOperationException("failed"), CancellationToken.None));
        Assert.Empty(client.Operations);
    }

    [Theory]
    [InlineData(1, "retry:42-0")]
    [InlineData(3, "complete:42-0")]
    [InlineData(5, "complete:42-0")]
    public async Task LeaseRetryUsesTheSharedAttemptPolicy(int deliveryCount, string expectedOperation)
    {
        var client = new FakeRedisStreamClient();
        client.Entries.Enqueue(new RedisStreamEntry("42-0", CreateItem().Serialize(), deliveryCount));
        IWorkItemConsumer<MessageWorkItem> consumer = CreateQueue(
            client,
            new WorkQueueOptions { MaxProcessingAttempts = 3 });
        var delivery = await ReadSingleDeliveryAsync(consumer, TestContext.Current.CancellationToken);
        await using var lease = delivery.Lease;

        Assert.Equal(
            LeaseOperationResult.Applied,
            await lease.RetryAsync(new InvalidOperationException("failed"), CancellationToken.None));
        Assert.Equal([expectedOperation], client.Operations);
    }

    [Fact]
    public async Task LeaseRenewalsAreRecordedByTheBackend()
    {
        var client = new FakeRedisStreamClient();
        client.Entries.Enqueue(new RedisStreamEntry("42-0", CreateItem().Serialize()));
        using var metrics = new SaucyBotMetrics();
        long renewals = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (ReferenceEquals(instrument, metrics.LeaseRenewed))
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, _, _) =>
        {
            renewals += measurement;
        });
        listener.Start();
        IWorkItemConsumer<MessageWorkItem> consumer = CreateQueue(
            client,
            new WorkQueueOptions { HeartbeatInterval = TimeSpan.FromMilliseconds(10) },
            metrics: metrics);
        var delivery = await ReadSingleDeliveryAsync(consumer, TestContext.Current.CancellationToken);
        await using var lease = delivery.Lease;

        await client.RenewObserved.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        for (var waited = 0; renewals == 0 && waited < 500; waited++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), TestContext.Current.CancellationToken);
        }

        Assert.True(renewals > 0);
    }

    [Fact]
    public async Task LeaseStopsRenewingWhenMaxProcessingTimeExpires()
    {
        var client = new FakeRedisStreamClient();
        client.Entries.Enqueue(new RedisStreamEntry("42-0", CreateItem().Serialize()));
        IWorkItemConsumer<MessageWorkItem> consumer = CreateQueue(
            client,
            new WorkQueueOptions
            {
                HeartbeatInterval = TimeSpan.FromMilliseconds(10),
                MaxProcessingTime = TimeSpan.FromMilliseconds(100),
            });
        var delivery = await ReadSingleDeliveryAsync(consumer, TestContext.Current.CancellationToken);
        await using var lease = delivery.Lease;

        await client.RenewObserved.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);
        var renewalsAfterDeadline = client.RenewCalls;
        await Task.Delay(TimeSpan.FromMilliseconds(150), TestContext.Current.CancellationToken);

        Assert.Equal(renewalsAfterDeadline, client.RenewCalls);
    }

    [Fact]
    public async Task LeaseSignalsLossWhenMaxProcessingTimeExpires()
    {
        var client = new FakeRedisStreamClient();
        client.Entries.Enqueue(new RedisStreamEntry("42-0", CreateItem().Serialize()));
        IWorkItemConsumer<MessageWorkItem> consumer = CreateQueue(
            client,
            new WorkQueueOptions
            {
                HeartbeatInterval = TimeSpan.FromMilliseconds(10),
                MaxProcessingTime = TimeSpan.FromMilliseconds(100),
            });
        var delivery = await ReadSingleDeliveryAsync(consumer, TestContext.Current.CancellationToken);
        await using var lease = delivery.Lease;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Task.Delay(Timeout.InfiniteTimeSpan, lease.LostToken)
                .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ProducerRetriesExplicitBackpressureUntilAccepted()
    {
        var client = new FakeRedisStreamClient { AddFailures = 2 };
        IWorkItemProducer<MessageWorkItem> producer = CreateQueue(
            client,
            redisOptions: new() { RetryDelay = TimeSpan.Zero });
        var item = CreateItem();

        var result = await producer.EnqueueAsync(
            item,
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);

        Assert.Equal(EnqueueResult.Accepted, result);
        Assert.Equal(3, client.AddCalls);
        Assert.Equal(item.Serialize(), client.Payloads.Single());
    }

    [Fact]
    public async Task NormalReadDoesNotReclaimPendingEntries()
    {
        var client = new FakeRedisStreamClient();
        client.Entries.Enqueue(new RedisStreamEntry("8-0", CreateItem().Serialize()));
        IWorkItemConsumer<MessageWorkItem> consumer = CreateQueue(
            client,
            redisOptions: new() { RetryDelay = TimeSpan.Zero });

        var delivery = await ReadSingleDeliveryAsync(consumer, TestContext.Current.CancellationToken);

        Assert.Equal("8-0", delivery.DeliveryId);
        Assert.Equal(1, client.NewReads);
        Assert.Equal(0, client.ReclaimCalls);
    }

    [Fact]
    public async Task ReadWithPreCanceledTokenThrowsInsteadOfEndingNormally()
    {
        var consumer = CreateQueue(new FakeRedisStreamClient());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await using var deliveries = consumer
            .ReadAsync("worker-1", cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => deliveries.MoveNextAsync().AsTask());
    }

    [Fact]
    public async Task StartupClearDelegatesOnlyWhenConfigured()
    {
        var client = new FakeRedisStreamClient();
        var queue = CreateQueue(client, new WorkQueueOptions { ClearPendingOnStartup = true });

        await queue.StartAsync(CancellationToken.None);

        Assert.Equal(1, client.EnsureGroupCalls);
        Assert.Equal(1, client.ClearCalls);
    }

    [Fact]
    public async Task StartupWithoutClearStillPreparesTheConsumerGroup()
    {
        var client = new FakeRedisStreamClient();
        var queue = CreateQueue(client);

        await queue.StartAsync(CancellationToken.None);

        Assert.Equal(1, client.EnsureGroupCalls);
        Assert.Equal(0, client.ClearCalls);
    }

    private static RedisWorkQueue CreateQueue(
        FakeRedisStreamClient client,
        WorkQueueOptions? options = null,
        RedisWorkQueueOptions? redisOptions = null,
        ISaucyBotMetrics? metrics = null) =>
        new(client, options ?? new WorkQueueOptions(), redisOptions ?? new RedisWorkQueueOptions(), metrics);

    private static MessageWorkItem CreateItem() => TestData.Message();

    private static async Task<WorkDelivery<MessageWorkItem>> ReadSingleDeliveryAsync(
        IWorkItemConsumer<MessageWorkItem> consumer,
        CancellationToken cancellationToken)
    {
        await using var deliveries = consumer
            .ReadAsync("worker-1", cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        Assert.True(await deliveries.MoveNextAsync());
        return deliveries.Current;
    }

    private sealed class FakeRedisStreamClient : IRedisStreamClient
    {
        public int AddFailures { get; set; }
        public int AddAmbiguousFailures { get; set; }
        public bool AddHangs { get; init; }
        public int AddCalls { get; private set; }
        public int ClearCalls { get; private set; }
        public int EnsureGroupCalls { get; private set; }
        public int NewReads { get; private set; }
        public int ReclaimCalls { get; private set; }
        public int RenewCalls { get; private set; }
        public bool RenewResult { get; set; } = true;
        public bool RenewHangs { get; init; }
        public TaskCompletionSource<bool> RenewCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? LastRenewConsumer { get; private set; }
        public string? LastRenewEntryId { get; private set; }
        public string? LastRenewToken { get; private set; }
        public LeaseOperationResult RetryResult { get; set; } = LeaseOperationResult.Applied;
        public int RetryUnknownResults { get; set; }
        public bool RetryHangs { get; init; }
        public TaskCompletionSource<LeaseOperationResult> RetryCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CompleteFailures { get; set; }
        public Exception? CompleteException { get; set; }
        public LeaseOperationResult CompleteResult { get; set; } = LeaseOperationResult.Applied;
        public int CompleteUnknownResults { get; set; }
        public TaskCompletionSource RenewObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<string> AddCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Queue<RedisStreamEntry> Entries { get; } = new();
        public Queue<RedisStreamEntry> ReclaimedEntries { get; } = new();
        public List<string> Payloads { get; } = [];
        public List<string> Acknowledged { get; } = [];
        public List<string> Deleted { get; } = [];
        public List<string> Operations { get; } = [];
        public List<string> CompleteTokens { get; } = [];
        public List<string> RetryTokens { get; } = [];
        public TaskCompletionSource CompleteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? CompleteCompletion { get; init; }
        public bool ReturnEntryAfterCancellation { get; init; }
        public bool ReturnNullAfterCancellation { get; init; }
        public TaskCompletionSource RemoteReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RemoteReadCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task EnsureGroupAsync(CancellationToken cancellationToken)
        {
            EnsureGroupCalls++;
            return Task.CompletedTask;
        }

        public Task<string> AddAsync(string payload, CancellationToken cancellationToken)
        {
            AddCalls++;
            if (AddHangs)
            {
                return AddCompletion.Task;
            }

            if (AddFailures-- > 0)
            {
                throw new RedisBackpressureException("queue full");
            }

            if (AddAmbiguousFailures-- > 0)
            {
                throw new RedisEnqueueAmbiguousException("enqueue outcome unknown");
            }

            Payloads.Add(payload);
            return Task.FromResult("1-0");
        }

        public async Task<RedisStreamEntry?> ReadNewAsync(
            string consumer,
            string leaseToken,
            CancellationToken cancellationToken)
        {
            NewReads++;
            if (ReturnEntryAfterCancellation)
            {
                RemoteReadStarted.TrySetResult();
                await RemoteReadCompletion.Task;
                return new RedisStreamEntry("claimed-0", CreateItem().Serialize());
            }

            if (ReturnNullAfterCancellation)
            {
                RemoteReadStarted.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
            }

            while (Entries.Count == 0)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return Entries.Dequeue();
        }

        public Task<bool> RenewAsync(
            string consumer,
            string entryId,
            string leaseToken,
            CancellationToken cancellationToken)
        {
            RenewCalls++;
            LastRenewConsumer = consumer;
            LastRenewEntryId = entryId;
            LastRenewToken = leaseToken;
            RenewObserved.TrySetResult();
            return RenewHangs ? RenewCompletion.Task : Task.FromResult(RenewResult);
        }

        public Task<RedisStreamEntry?> ReclaimAsync(
            string consumer,
            TimeSpan minimumIdleTime,
            string leaseToken,
            CancellationToken cancellationToken)
        {
            ReclaimCalls++;
            return Task.FromResult(ReclaimedEntries.Count > 0 ? ReclaimedEntries.Dequeue() : null);
        }

        public async Task<LeaseOperationResult> CompleteAsync(
            string consumer,
            string entryId,
            string leaseToken,
            CancellationToken cancellationToken)
        {
            Operations.Add($"complete:{entryId}");
            CompleteTokens.Add(leaseToken);
            CompleteStarted.TrySetResult();
            if (CompleteCompletion is not null)
            {
                await CompleteCompletion.Task;
            }

            if (CompleteUnknownResults-- > 0)
            {
                return LeaseOperationResult.OutcomeUnknown;
            }

            if (CompleteFailures-- > 0)
            {
                throw new InvalidOperationException("transient completion failure");
            }

            if (CompleteException is not null)
            {
                throw CompleteException;
            }

            if (CompleteResult is LeaseOperationResult.Applied or LeaseOperationResult.AlreadyApplied)
            {
                Acknowledged.Add(entryId);
                Deleted.Add(entryId);
            }

            return CompleteResult;
        }

        public Task<LeaseOperationResult> RetryAsync(
            string consumer,
            string entryId,
            string leaseToken,
            CancellationToken cancellationToken)
        {
            Operations.Add($"retry:{entryId}");
            RetryTokens.Add(leaseToken);
            if (RetryHangs)
            {
                return RetryCompletion.Task;
            }

            if (RetryUnknownResults-- > 0)
            {
                return Task.FromResult(LeaseOperationResult.OutcomeUnknown);
            }

            return Task.FromResult(RetryResult);
        }

        public Task ClearPendingAsync(CancellationToken cancellationToken)
        {
            ClearCalls++;
            return Task.CompletedTask;
        }
    }
}
