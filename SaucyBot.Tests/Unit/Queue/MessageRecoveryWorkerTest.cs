using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
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
        var logger = new TestLogger<MessageQueueReader>();
        var reader = new MessageQueueReader(consumer, channel, logger);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var running = reader.RunAsync("reader-1", cancellation.Token);
        Assert.Equal(0, consumer.ReadCalls);

        await using var readerOutput = channel.ReadAllAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await readerOutput.MoveNextAsync());
        Assert.Equal("initial-0", readerOutput.Current.DeliveryId);
        Assert.True(await readerOutput.MoveNextAsync());
        Assert.Equal("initial-1", readerOutput.Current.DeliveryId);
        await consumer.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.True(await readerOutput.MoveNextAsync().AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal("new-0", readerOutput.Current.DeliveryId);
        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Debug && entry.Message.Contains("read new message delivery", StringComparison.OrdinalIgnoreCase));

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
        var logger = new TestLogger<MessageRecoveryWorker>();
        var worker = new MessageRecoveryWorker(
            consumer,
            channel,
            new WorkQueueOptions { PendingMessageIdleTime = TimeSpan.Zero, ReclaimerInterval = TimeSpan.FromSeconds(10) },
            logger,
            new SaucyBotMetrics());
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var running = worker.RunAsync("recovery-1", cancellation.Token);

        Assert.Equal(0, consumer.RecoveryCalls);

        await using var recoveryOutput = channel.ReadAllAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await recoveryOutput.MoveNextAsync());
        Assert.Equal("initial-0", recoveryOutput.Current.DeliveryId);
        Assert.True(await recoveryOutput.MoveNextAsync());
        Assert.Equal("initial-1", recoveryOutput.Current.DeliveryId);
        await consumer.RecoveryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.True(await recoveryOutput.MoveNextAsync().AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal("recovered-0", recoveryOutput.Current.DeliveryId);
        Assert.True(recoveryOutput.Current.IsRecovered);
        Assert.True(consumer.RecoveryCalls >= 1);
        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Debug && entry.Message.Contains("claimed recovered message delivery", StringComparison.OrdinalIgnoreCase));

        cancellation.Cancel();
        await running.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    private static WorkDelivery<MessageWorkItem> CreateDelivery(string id) => new(
        TestData.Message(),
        id,
        1,
        DateTimeOffset.UtcNow,
        TestData.NoOpLease());

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
