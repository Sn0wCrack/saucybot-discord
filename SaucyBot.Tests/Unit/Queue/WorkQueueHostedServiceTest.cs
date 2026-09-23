using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SaucyBot.Diagnostics;
using SaucyBot.Queue;
using Xunit;

namespace SaucyBot.Tests.Unit.Queue;

public sealed class WorkQueueHostedServiceTest
{
    [Fact]
    public async Task CancellationAfterNonCooperativeProcessingLeavesItemPending()
    {
        var queue = new TestWorkQueue();
        var processor = new NonCooperativeProcessor();
        var item = CreateItem("1-0");
        queue.Add(item);

        await using var service = CreateService(queue, processor, TimeSpan.FromMilliseconds(25));
        await service.StartAsync(TestContext.Current.CancellationToken);
        await processor.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await service.StopAsync(TestContext.Current.CancellationToken);
        processor.Release();
        await processor.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Empty(queue.Acknowledged);
    }

    [Fact]
    public async Task DisposalDoesNotRaceWithTimedOutWorkers()
    {
        var queue = new TestWorkQueue();
        var processor = new NonCooperativeProcessor();
        queue.Add(CreateItem("1-0"));

        var service = CreateService(queue, processor, TimeSpan.FromMilliseconds(25));
        await service.StartAsync(TestContext.Current.CancellationToken);
        await processor.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await service.StopAsync(TestContext.Current.CancellationToken);
        Assert.False(service.WorkerCompletion.IsCompleted);

        processor.Release();
        await service.WorkerCompletion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await service.DisposeAsync();
    }

    [Fact]
    public async Task PreCanceledStopCancelsNonCooperativeWorkersBeforeReturning()
    {
        var queue = new TestWorkQueue();
        var processor = new NonCooperativeProcessor();
        queue.Add(CreateItem("1-0"));

        var service = CreateService(queue, processor, TimeSpan.FromSeconds(1));
        await service.StartAsync(TestContext.Current.CancellationToken);
        await processor.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.StopAsync(cancellation.Token));
        await processor.CancellationObservedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        processor.Release();
        await service.WorkerCompletion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await service.DisposeAsync();

        Assert.Empty(queue.Acknowledged);
    }

    [Fact]
    public async Task HostStoppingStopsIntakeButAllowsActiveWorkToDrain()
    {
        var queue = new TestWorkQueue();
        var processor = new NonCooperativeProcessor();
        queue.Add(CreateItem("1-0"));

        await using var service = CreateService(queue, processor, TimeSpan.FromSeconds(1));
        using var stopping = new CancellationTokenSource();
        var executeAsync = typeof(WorkQueueHostedService)
            .GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var execution = (Task)executeAsync.Invoke(service, [stopping.Token])!;

        await processor.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        stopping.Cancel();
        Assert.True(service.AdmissionToken.IsCancellationRequested);
        Assert.False(processor.CancellationObserved);

        processor.Release();
        await queue.ReadCancellationObservedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await execution.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(queue.ReadCancellationObserved);
        Assert.Single(queue.Acknowledged);
    }

    [Fact]
    public async Task ProcessingFailureAtAttemptLimitAcknowledgesAndDeletesItem()
    {
        var queue = new TestWorkQueue();
        queue.Add(CreateItem("1-0", deliveryCount: 2));
        var processor = new FailingProcessor();

        await using var service = CreateService(
            queue,
            processor,
            TimeSpan.FromSeconds(1),
            maxProcessingAttempts: 2);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await queue.AcknowledgedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Single(queue.Acknowledged);
        Assert.Equal(1, processor.Attempts);
    }

    [Fact]
    public async Task WorkerReadFailureRestartsAndProcessesLaterItem()
    {
        var queue = new TestWorkQueue { FailFirstRead = true };
        var processor = new RecordingProcessor();
        queue.Add(CreateItem("2-0"));

        await using var service = CreateService(queue, processor, TimeSpan.FromSeconds(5));
        await service.StartAsync(TestContext.Current.CancellationToken);
        await processor.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, queue.ReadCalls);
        Assert.Single(queue.Acknowledged);
    }

    [Fact]
    public async Task RecoveryLoopProcessesReclaimedItem()
    {
        var queue = new TestWorkQueue();
        var processor = new RecordingProcessor();
        var recovered = CreateItem("recovered-0");
        queue.Reclaimed.Enqueue(recovered);
        var executor = new RecordingExecutor();

        await using var service = CreateService(
            queue,
            processor,
            TimeSpan.FromSeconds(5),
            executor: executor,
            reclaimerInterval: TimeSpan.FromMilliseconds(10));
        await service.StartAsync(TestContext.Current.CancellationToken);
        await executor.Processed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal([recovered], executor.Items);
    }

    [Fact]
    public async Task StoppingAfterWorkerFailureDoesNotRestartTheWorker()
    {
        var queue = new TestWorkQueue { FailFirstRead = true };
        var processor = new RecordingProcessor();

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
        IQueuedWorkItemExecutor? executor = null,
        TimeSpan? reclaimerInterval = null) => new(
        queue,
        processor,
        new WorkQueueOptions
        {
            MessageWorkerCount = 1,
            InteractionWorkerCount = 0,
            ShutdownDrainTimeout = shutdownDrainTimeout,
            MaxProcessingAttempts = maxProcessingAttempts,
            Redis = new() { ReclaimerInterval = reclaimerInterval ?? TimeSpan.FromSeconds(5) }
        },
        NullLogger<WorkQueueHostedService>.Instance,
        new InteractionWorkChannel(new WorkQueueOptions()),
        Substitute.For<IInteractionProcessor>(),
        new SaucyBotMetrics(),
        executor);

    private static QueuedMessageWorkItem CreateItem(string entryId, int deliveryCount = 1) => new(
        entryId,
        new MessageWorkItem(1, 2, 3, 4, [], "content", null, [], true, true,
            Guid.Parse("11111111-1111-1111-1111-111111111111")),
        deliveryCount);

    private sealed class NonCooperativeProcessor : IWorkItemProcessor
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSignal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancellationObservedSignal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CancellationObserved { get; private set; }

        public async Task ProcessAsync(QueuedMessageWorkItem item, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            if (cancellationToken.IsCancellationRequested)
            {
                CancellationObserved = true;
                CancellationObservedSignal.TrySetResult();
            }

            using var cancellationRegistration = cancellationToken.Register(() =>
            {
                CancellationObserved = true;
                CancellationObservedSignal.TrySetResult();
            });

            await ReleaseSignal.Task;
            Completed.TrySetResult();
        }

        public void Release() => ReleaseSignal.TrySetResult();
    }

    private sealed class FailingProcessor : IWorkItemProcessor
    {
        public int Attempts { get; private set; }

        public Task ProcessAsync(QueuedMessageWorkItem item, CancellationToken cancellationToken)
        {
            Attempts++;
            return Task.FromException(new InvalidOperationException("processing failed"));
        }
    }

    private sealed class RecordingProcessor : IWorkItemProcessor
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ProcessAsync(QueuedMessageWorkItem item, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingExecutor : IQueuedWorkItemExecutor
    {
        public List<QueuedMessageWorkItem> Items { get; } = [];
        public TaskCompletionSource Processed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ExecuteAsync(string consumer, QueuedMessageWorkItem item, CancellationToken cancellationToken)
        {
            Items.Add(item);
            Processed.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class TestWorkQueue : IMessageWorkQueue
    {
        private readonly Channel<QueuedMessageWorkItem> _items = Channel.CreateUnbounded<QueuedMessageWorkItem>();

        public List<QueuedMessageWorkItem> Acknowledged { get; } = [];
        public TaskCompletionSource AcknowledgedSignal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool ReadCancellationObserved { get; private set; }
        public TaskCompletionSource ReadCancellationObservedSignal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReadFailureObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Queue<QueuedMessageWorkItem> Reclaimed { get; } = new();
        public bool FailFirstRead { get; init; }
        public int ReadCalls { get; private set; }

        public void Add(QueuedMessageWorkItem item) => _items.Writer.TryWrite(item);

        public Task EnqueueAsync(MessageWorkItem item, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<QueuedMessageWorkItem> ReadAsync(
            string consumer,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            ReadCalls++;
            if (FailFirstRead && ReadCalls == 1)
            {
                ReadFailureObserved.TrySetResult();
                throw new InvalidOperationException("simulated read failure");
            }

            while (true)
            {
                QueuedMessageWorkItem item;
                try
                {
                    item = await _items.Reader.ReadAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    ReadCancellationObserved = true;
                    ReadCancellationObservedSignal.TrySetResult();
                    throw;
                }

                yield return item;
            }
        }

        public async IAsyncEnumerable<QueuedMessageWorkItem> ReclaimAsync(
            string consumer,
            TimeSpan minimumIdleTime,
            int count,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            while (Reclaimed.Count > 0)
            {
                yield return Reclaimed.Dequeue();
            }

            await Task.CompletedTask;
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task CompleteAsync(QueuedMessageWorkItem item, CancellationToken cancellationToken)
        {
            Acknowledged.Add(item);
            AcknowledgedSignal.TrySetResult();
            return Task.CompletedTask;
        }

        public async Task<WorkItemFailureResult> FailAsync(
            QueuedMessageWorkItem item,
            Exception exception,
            CancellationToken cancellationToken)
        {
            await CompleteAsync(item, cancellationToken);
            return new WorkItemFailureResult(WorkItemFailureAction.Discarded, item.DeliveryCount);
        }
    }
}
