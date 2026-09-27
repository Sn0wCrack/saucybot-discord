using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SaucyBot.Diagnostics;
using SaucyBot.Queue;
using SaucyBot.Tests.Unit.Common;
using Xunit;

namespace SaucyBot.Tests.Unit.Queue;

public sealed class WorkQueueHostedServiceTest
{
    [Fact]
    public async Task CancellationAfterNonCooperativeProcessingLeavesItemPending()
    {
        var queue = new TestWorkQueue();
        var processor = Substitute.For<IWorkItemProcessor>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        processor.ProcessAsync(Arg.Any<MessageWorkItem>(), Arg.Any<CancellationToken>())
            .Returns(call => ProcessUntilReleasedAsync(call.Arg<CancellationToken>()));
        var item = CreateItem("1-0");
        queue.Add(item);

        await using var service = CreateService(queue, processor, TimeSpan.FromMilliseconds(25));
        await service.StartAsync(TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await service.StopAsync(TestContext.Current.CancellationToken);
        release.TrySetResult();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Empty(queue.Acknowledged);

        async Task ProcessUntilReleasedAsync(CancellationToken cancellationToken)
        {
            started.TrySetResult();
            using var registration = cancellationToken.Register(() => cancellationObserved.TrySetResult());
            await release.Task;
            completed.TrySetResult();
        }
    }

    [Fact]
    public async Task PreCanceledStopCancelsNonCooperativeWorkersBeforeReturning()
    {
        var queue = new TestWorkQueue();
        var processor = Substitute.For<IWorkItemProcessor>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        processor.ProcessAsync(Arg.Any<MessageWorkItem>(), Arg.Any<CancellationToken>())
            .Returns(call => ProcessUntilReleasedAsync(call.Arg<CancellationToken>()));
        queue.Add(CreateItem("1-0"));

        var service = CreateService(queue, processor, TimeSpan.FromSeconds(1));
        await service.StartAsync(TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.StopAsync(cancellation.Token));
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        release.TrySetResult();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);
        await service.DisposeAsync();

        Assert.Empty(queue.Acknowledged);

        async Task ProcessUntilReleasedAsync(CancellationToken cancellationToken)
        {
            started.TrySetResult();
            using var registration = cancellationToken.Register(() => cancellationObserved.TrySetResult());
            await release.Task;
            completed.TrySetResult();
        }
    }

    [Fact]
    public async Task ProcessingFailureAtAttemptLimitAcknowledgesAndDeletesItem()
    {
        var queue = new TestWorkQueue();
        queue.Add(CreateItem("1-0", deliveryCount: 2));
        var processor = Substitute.For<IWorkItemProcessor>();
        processor.ProcessAsync(Arg.Any<MessageWorkItem>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("processing failed")));

        await using var service = CreateService(
            queue,
            processor,
            TimeSpan.FromSeconds(1),
            maxProcessingAttempts: 2);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await queue.AcknowledgedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Single(queue.Acknowledged);
        await processor.Received(1).ProcessAsync(Arg.Any<MessageWorkItem>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WorkerReadFailureRestartsAndProcessesLaterItem()
    {
        var queue = new TestWorkQueue { FailFirstRead = true };
        var processor = Substitute.For<IWorkItemProcessor>();
        var item = CreateItem("2-0");
        queue.Add(item);
        var processed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        processor.ProcessAsync(Arg.Any<MessageWorkItem>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                processed.TrySetResult();
                return Task.CompletedTask;
            });

        await using var service = CreateService(queue, processor, TimeSpan.FromSeconds(5));
        await service.StartAsync(TestContext.Current.CancellationToken);
        await processed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await processor.Received(1).ProcessAsync(item.Item, Arg.Any<CancellationToken>());
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, queue.ReadCalls);
        Assert.Single(queue.Acknowledged);
    }

    [Fact]
    public async Task RecoveryLoopProcessesReclaimedItem()
    {
        var queue = new TestWorkQueue();
        var processor = Substitute.For<IWorkItemProcessor>();
        var recovered = CreateItem("recovered-0");
        queue.AddRecovered(recovered);
        MessageWorkItem? processed = null;
        var processedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        processor.ProcessAsync(Arg.Any<MessageWorkItem>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                processed = call.Arg<MessageWorkItem>();
                processedSignal.TrySetResult();
                return Task.CompletedTask;
            });

        await using var service = CreateService(
            queue,
            processor,
            TimeSpan.FromSeconds(5),
            reclaimerInterval: TimeSpan.FromMilliseconds(10));
        await service.StartAsync(TestContext.Current.CancellationToken);
        await processedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Same(recovered.Item, processed);
    }

    [Fact]
    public async Task StoppingAfterWorkerFailureDoesNotRestartTheWorker()
    {
        var queue = new TestWorkQueue { FailFirstRead = true };
        var processor = Substitute.For<IWorkItemProcessor>();

        await using var service = CreateService(queue, processor, TimeSpan.FromSeconds(5));
        await service.StartAsync(TestContext.Current.CancellationToken);
        await queue.ReadFailureObserved.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, queue.ReadCalls);
    }

    private static WorkQueueHostedService CreateService(
        TestWorkQueue queue,
        IWorkItemProcessor processor,
        TimeSpan shutdownDrainTimeout,
        int maxProcessingAttempts = 3,
        TimeSpan? reclaimerInterval = null)
    {
        var options = new WorkQueueOptions
        {
            MessageWorkerCount = 1,
            InteractionWorkerCount = 0,
            ShutdownDrainTimeout = shutdownDrainTimeout,
            MaxProcessingAttempts = maxProcessingAttempts,
            ReclaimerInterval = reclaimerInterval ?? TimeSpan.FromSeconds(5),
        };
        queue.MaxProcessingAttempts = maxProcessingAttempts;
        var channel = new MessageDeliveryChannel(options);
        var metrics = new SaucyBotMetrics();
        var pipeline = new QueueMiddlewarePipeline<MessageWorkItem>(
            [new QueueMetricsMiddleware<MessageWorkItem>(metrics)]);
        var interactionChannel = new InteractionWorkChannel(options);
        var interactionWorker = new InteractionQueueWorker(
            interactionChannel,
            new QueueMiddlewarePipeline<IInteractionWorkItem>([]),
            Substitute.For<IInteractionProcessor>(),
            NullLogger<InteractionQueueWorker>.Instance,
            metrics);

        return new WorkQueueHostedService(
            queue,
            channel,
            new MessageQueueReader(queue, channel, NullLogger<MessageQueueReader>.Instance),
            new MessageRecoveryWorker(queue, channel, options, NullLogger<MessageRecoveryWorker>.Instance, metrics),
            new MessageQueueWorker(channel, pipeline, processor, options, NullLogger<MessageQueueWorker>.Instance, metrics),
            options,
            NullLogger<WorkQueueHostedService>.Instance,
            interactionChannel,
            interactionWorker,
            metrics);
    }

    private static WorkDelivery<MessageWorkItem> CreateItem(string entryId, int deliveryCount = 1) => new(
        new MessageWorkItem(1, 2, 3, 4, [], "content", null, [], true, true,
            Guid.Parse("11111111-1111-1111-1111-111111111111")),
        entryId,
        deliveryCount,
        DateTimeOffset.UtcNow,
        TestData.NoOpLease());

    private sealed class TestWorkQueue : IWorkItemConsumer<MessageWorkItem>
    {
        private readonly Channel<WorkDelivery<MessageWorkItem>> _items = Channel.CreateUnbounded<WorkDelivery<MessageWorkItem>>();

        public List<string> Acknowledged { get; } = [];
        public TaskCompletionSource AcknowledgedSignal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool ReadCancellationObserved { get; private set; }
        public TaskCompletionSource ReadCancellationObservedSignal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReadFailureObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Queue<WorkDelivery<MessageWorkItem>> Reclaimed { get; } = new();
        public bool FailFirstRead { get; init; }
        public int ReadCalls { get; private set; }
        public int StartCalls { get; private set; }
        public int MaxProcessingAttempts { get; set; } = 3;

        public void Add(WorkDelivery<MessageWorkItem> delivery) => _items.Writer.TryWrite(AttachLease(delivery));

        public void AddRecovered(WorkDelivery<MessageWorkItem> delivery) => Reclaimed.Enqueue(AttachLease(delivery));

        public async IAsyncEnumerable<WorkDelivery<MessageWorkItem>> ReadAsync(
            string consumer,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            ReadCalls++;
            if (FailFirstRead && ReadCalls == 1)
            {
                ReadFailureObserved.TrySetResult();
                throw new InvalidOperationException("simulated read failure");
            }

            using var registration = cancellationToken.Register(() =>
            {
                ReadCancellationObserved = true;
                ReadCancellationObservedSignal.TrySetResult();
            });
            while (true)
            {
                WorkDelivery<MessageWorkItem> delivery;
                try
                {
                    delivery = await _items.Reader.ReadAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    ReadCancellationObserved = true;
                    ReadCancellationObservedSignal.TrySetResult();
                    throw;
                }

                yield return delivery;
            }
        }

        public async IAsyncEnumerable<WorkDelivery<MessageWorkItem>> RecoverAsync(
            string consumer,
            TimeSpan minimumIdleTime,
            int count,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            for (var i = 0; i < Math.Min(count, Reclaimed.Count); i++)
            {
                yield return Reclaimed.Dequeue();
            }

            await Task.CompletedTask;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            StartCalls++;
            return Task.CompletedTask;
        }

        private WorkDelivery<MessageWorkItem> AttachLease(WorkDelivery<MessageWorkItem> delivery)
        {
            var lease = Substitute.For<IWorkItemLease>();
            lease.LostToken.Returns(CancellationToken.None);
            lease.CompleteAsync(Arg.Any<CancellationToken>())
                .Returns(call => CompleteAsync(delivery, call.Arg<CancellationToken>()));
            lease.RetryAsync(Arg.Any<Exception>(), Arg.Any<CancellationToken>())
                .Returns(call => RetryAsync(delivery, call.Arg<CancellationToken>()));
            lease.DisposeAsync().Returns(ValueTask.CompletedTask);
            return delivery with { Lease = lease };
        }

        private void Acknowledge(WorkDelivery<MessageWorkItem> delivery)
        {
            Acknowledged.Add(delivery.DeliveryId);
            AcknowledgedSignal.TrySetResult();
        }

        private Task<LeaseOperationResult> CompleteAsync(
            WorkDelivery<MessageWorkItem> delivery,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (AcknowledgeFailure is not null)
            {
                return Task.FromException<LeaseOperationResult>(AcknowledgeFailure);
            }

            Acknowledge(delivery);
            return Task.FromResult(LeaseOperationResult.Applied);
        }

        private Task<LeaseOperationResult> RetryAsync(
            WorkDelivery<MessageWorkItem> delivery,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return delivery.Attempt >= MaxProcessingAttempts
                ? CompleteAsync(delivery, cancellationToken)
                : Task.FromResult(LeaseOperationResult.Applied);
        }

        public Exception? AcknowledgeFailure { get; init; }
    }
}
