using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    public void HostedServiceExposesOnlyTheLeaseAwareConstructor()
    {
        var constructors = typeof(WorkQueueHostedService).GetConstructors();

        var constructor = Assert.Single(constructors);
        var parameterTypes = constructor.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        Assert.Contains(typeof(IWorkItemConsumer<MessageWorkItem>), parameterTypes);
        Assert.Contains(typeof(MessageQueueWorker), parameterTypes);
        Assert.DoesNotContain(typeof(IWorkItemProducer<MessageWorkItem>), parameterTypes);
    }

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
        queue.AddRecovered(recovered);

        await using var service = CreateService(
            queue,
            processor,
            TimeSpan.FromSeconds(5),
            reclaimerInterval: TimeSpan.FromMilliseconds(10));
        await service.StartAsync(TestContext.Current.CancellationToken);
        await processor.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(recovered.Item, processor.Item);
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

        return new WorkQueueHostedService(
            queue,
            channel,
            new MessageQueueReader(queue, channel, NullLogger<MessageQueueReader>.Instance),
            new MessageRecoveryWorker(queue, channel, options, NullLogger<MessageRecoveryWorker>.Instance, metrics),
            new MessageQueueWorker(channel, pipeline, processor, options, NullLogger<MessageQueueWorker>.Instance, metrics),
            options,
            NullLogger<WorkQueueHostedService>.Instance,
            new InteractionWorkChannel(options),
            Substitute.For<IInteractionProcessor>(),
            metrics);
    }

    private static WorkDelivery<MessageWorkItem> CreateItem(string entryId, int deliveryCount = 1) => new(
        new MessageWorkItem(1, 2, 3, 4, [], "content", null, [], true, true,
            Guid.Parse("11111111-1111-1111-1111-111111111111")),
        entryId,
        deliveryCount,
        DateTimeOffset.UtcNow,
        new TestData.NoOpWorkItemLease());

    private sealed class NonCooperativeProcessor : IWorkItemProcessor
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSignal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancellationObservedSignal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CancellationObserved { get; private set; }

        public async Task ProcessAsync(MessageWorkItem item, CancellationToken cancellationToken)
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

        public Task ProcessAsync(MessageWorkItem item, CancellationToken cancellationToken)
        {
            Attempts++;
            return Task.FromException(new InvalidOperationException("processing failed"));
        }
    }

    private sealed class RecordingProcessor : IWorkItemProcessor
    {
        public MessageWorkItem? Item { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ProcessAsync(MessageWorkItem item, CancellationToken cancellationToken)
        {
            Item = item;
            Started.TrySetResult();
            return Task.CompletedTask;
        }
    }

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
        public Action? OnStart { get; set; }

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
            OnStart?.Invoke();
            return Task.CompletedTask;
        }

        private WorkDelivery<MessageWorkItem> AttachLease(WorkDelivery<MessageWorkItem> delivery) =>
            delivery with { Lease = new TestWorkItemLease(this, delivery) };

        private void Acknowledge(WorkDelivery<MessageWorkItem> delivery)
        {
            Acknowledged.Add(delivery.DeliveryId);
            AcknowledgedSignal.TrySetResult();
        }

        private sealed class TestWorkItemLease(TestWorkQueue owner, WorkDelivery<MessageWorkItem> delivery) : IWorkItemLease
        {
            private readonly CancellationTokenSource _lost = new();
            public CancellationToken LostToken => _lost.Token;

            public Task<LeaseOperationResult> CompleteAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (owner.AcknowledgeFailure is not null)
                {
                    return Task.FromException<LeaseOperationResult>(owner.AcknowledgeFailure);
                }

                owner.Acknowledge(delivery);
                return Task.FromResult(LeaseOperationResult.Applied);
            }

            public Task<LeaseOperationResult> RetryAsync(Exception exception, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (delivery.Attempt >= owner.MaxProcessingAttempts)
                {
                    return CompleteAsync(cancellationToken);
                }

                return Task.FromResult(LeaseOperationResult.Applied);
            }

            public ValueTask DisposeAsync()
            {
                _lost.Dispose();
                return ValueTask.CompletedTask;
            }
        }

        public Exception? AcknowledgeFailure { get; init; }
    }
}
