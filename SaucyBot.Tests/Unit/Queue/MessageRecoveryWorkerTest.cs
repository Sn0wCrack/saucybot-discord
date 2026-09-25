using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SaucyBot.Diagnostics;
using SaucyBot.Queue;
using SaucyBot.Tests.Unit.Common;
using Xunit;

namespace SaucyBot.Tests.Unit.Queue;

public sealed class MessageRecoveryWorkerTest
{
    [Fact]
    public async Task ReaderReservesHandoffCapacityBeforeReadingFromBackend()
    {
        var channel = new MessageDeliveryChannel(new WorkQueueOptions { RecoveryHandoffCapacity = 2 });
        await using (var firstReservation = await channel.ReserveAsync(TestContext.Current.CancellationToken))
        {
            firstReservation.Publish(CreateDelivery("initial-0"));
        }
        await using (var secondReservation = await channel.ReserveAsync(TestContext.Current.CancellationToken))
        {
            secondReservation.Publish(CreateDelivery("initial-1"));
        }

        var consumer = new FakeConsumer(CreateDelivery("new-0"));
        var reader = new MessageQueueReader(consumer, channel, NullLogger<MessageQueueReader>.Instance);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var running = reader.RunAsync("reader-1", cancellation.Token);
        Assert.Equal(0, consumer.ReadCalls);

        var first = await channel.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal("initial-0", first.DeliveryId);
        var second = await channel.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal("initial-1", second.DeliveryId);
        await consumer.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var next = await channel.ReadAsync(TestContext.Current.CancellationToken)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal("new-0", next.DeliveryId);

        cancellation.Cancel();
        await running.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RecoveryReservesHandoffCapacityBeforeClaimingAndPublishesOneDelivery()
    {
        var channel = new MessageDeliveryChannel(new WorkQueueOptions { RecoveryHandoffCapacity = 2 });
        await using (var firstReservation = await channel.ReserveAsync(TestContext.Current.CancellationToken))
        {
            firstReservation.Publish(CreateDelivery("initial-0"));
        }
        await using (var secondReservation = await channel.ReserveAsync(TestContext.Current.CancellationToken))
        {
            secondReservation.Publish(CreateDelivery("initial-1"));
        }

        var recovered = CreateDelivery("recovered-0") with { IsRecovered = true };
        var consumer = new FakeConsumer(recovered);
        var worker = new MessageRecoveryWorker(
            consumer,
            channel,
            new WorkQueueOptions { PendingMessageIdleTime = TimeSpan.Zero, ReclaimerInterval = TimeSpan.FromSeconds(10) },
            NullLogger<MessageRecoveryWorker>.Instance,
            new SaucyBotMetrics());
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var running = worker.RunAsync("recovery-1", cancellation.Token);

        Assert.Equal(0, consumer.RecoveryCalls);

        var first = await channel.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal("initial-0", first.DeliveryId);
        var second = await channel.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal("initial-1", second.DeliveryId);
        await consumer.RecoveryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var next = await channel.ReadAsync(TestContext.Current.CancellationToken)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal("recovered-0", next.DeliveryId);
        Assert.True(next.IsRecovered);
        Assert.True(consumer.RecoveryCalls >= 1);

        cancellation.Cancel();
        await running.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    private static WorkDelivery<MessageWorkItem> CreateDelivery(string id) => new(
        TestData.Message(),
        id,
        1,
        DateTimeOffset.UtcNow,
        new TestData.NoOpWorkItemLease());

    private sealed class FakeConsumer(WorkDelivery<MessageWorkItem> recovered) : IWorkItemConsumer<MessageWorkItem>
    {
        public int RecoveryCalls { get; private set; }
        public int ReadCalls { get; private set; }
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RecoveryStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async IAsyncEnumerable<WorkDelivery<MessageWorkItem>> ReadAsync(
            string consumer,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            ReadCalls++;
            ReadStarted.TrySetResult();
            await Task.CompletedTask;
            yield return recovered;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public async IAsyncEnumerable<WorkDelivery<MessageWorkItem>> RecoverAsync(
            string consumer,
            TimeSpan minimumIdleTime,
            int count,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            RecoveryCalls++;
            RecoveryStarted.TrySetResult();
            yield return recovered;
            await Task.CompletedTask;
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
