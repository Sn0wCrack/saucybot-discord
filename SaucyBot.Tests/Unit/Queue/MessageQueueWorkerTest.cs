using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SaucyBot.Diagnostics;
using SaucyBot.Queue;
using SaucyBot.Tests.Unit.Common;
using Xunit;

namespace SaucyBot.Tests.Unit.Queue;

public sealed class MessageQueueWorkerTest
{
    [Fact]
    public async Task WorkerRunsPipelineAndCompletesLeaseAfterHandlerSuccess()
    {
        using var metrics = new SaucyBotMetrics();
        using var listener = new MeterListener();
        long handlerOutcomes = 0;
        long legacySuccesses = 0;
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (ReferenceEquals(instrument, metrics.HandlerOutcomes) || ReferenceEquals(instrument, metrics.Succeeded))
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, _, _) =>
        {
            if (ReferenceEquals(instrument, metrics.HandlerOutcomes))
            {
                handlerOutcomes += measurement;
            }

            if (ReferenceEquals(instrument, metrics.Succeeded))
            {
                legacySuccesses += measurement;
            }
        });
        listener.Start();

        var channel = new MessageDeliveryChannel(new WorkQueueOptions { RecoveryHandoffCapacity = 2 });
        var lease = new RecordingLease();
        await using (var reservation = await channel.ReserveAsync(TestContext.Current.CancellationToken))
        {
            reservation.Publish(new WorkDelivery<MessageWorkItem>(
                TestData.Message(),
                "entry-1",
                1,
                DateTimeOffset.UtcNow,
                lease));
        }

        var processor = new RecordingProcessor();
        var pipeline = new QueueMiddlewarePipeline<MessageWorkItem>(
            [new QueueMetricsMiddleware<MessageWorkItem>(metrics)]);
        var worker = new MessageQueueWorker(
            channel,
            pipeline,
            processor,
            new WorkQueueOptions { MaxProcessingTime = TimeSpan.FromSeconds(10) },
            NullLogger<MessageQueueWorker>.Instance,
            metrics);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var running = worker.RunSupervisedAsync("message-1", cancellation.Token);
        await processor.Processed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await lease.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cancellation.Cancel();
        channel.Complete();
        await running.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(TestData.Message().MessageId, processor.Item!.MessageId);
        Assert.Equal(1, lease.CompleteCalls);
        Assert.Equal(0, lease.RetryCalls);
        Assert.Equal(1, handlerOutcomes);
        Assert.Equal(0, legacySuccesses);
    }

    [Fact]
    public async Task WorkerRetriesHandlerFailureThroughTheLease()
    {
        var channel = new MessageDeliveryChannel(new WorkQueueOptions { RecoveryHandoffCapacity = 2 });
        var lease = new RecordingLease();
        await using (var reservation = await channel.ReserveAsync(TestContext.Current.CancellationToken))
        {
            reservation.Publish(new WorkDelivery<MessageWorkItem>(
                TestData.Message(),
                "entry-2",
                1,
                DateTimeOffset.UtcNow,
                lease));
        }

        var processor = new RecordingProcessor { Failure = new InvalidOperationException("handler failed") };
        var worker = new MessageQueueWorker(
            channel,
            new QueueMiddlewarePipeline<MessageWorkItem>([]),
            processor,
            new WorkQueueOptions { MaxProcessingTime = TimeSpan.FromSeconds(10) },
            NullLogger<MessageQueueWorker>.Instance,
            new SaucyBotMetrics());
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var running = worker.RunSupervisedAsync("message-1", cancellation.Token);
        await lease.Retried.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cancellation.Cancel();
        channel.Complete();
        await running.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(0, lease.CompleteCalls);
        Assert.Equal(1, lease.RetryCalls);
    }

    [Fact]
    public async Task WorkerDoesNotCompleteOrRetryAfterLeaseLoss()
    {
        var channel = new MessageDeliveryChannel(new WorkQueueOptions { RecoveryHandoffCapacity = 2 });
        var lease = new RecordingLease();
        await using (var reservation = await channel.ReserveAsync(TestContext.Current.CancellationToken))
        {
            reservation.Publish(new WorkDelivery<MessageWorkItem>(
                TestData.Message(),
                "entry-3",
                1,
                DateTimeOffset.UtcNow,
                lease));
        }

        var processor = new BlockingProcessor();
        var worker = new MessageQueueWorker(
            channel,
            new QueueMiddlewarePipeline<MessageWorkItem>([]),
            processor,
            new WorkQueueOptions { MaxProcessingTime = TimeSpan.FromSeconds(10) },
            NullLogger<MessageQueueWorker>.Instance,
            new SaucyBotMetrics());
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var running = worker.RunSupervisedAsync("message-1", cancellation.Token);
        await processor.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        lease.Lose();
        await lease.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cancellation.Cancel();
        channel.Complete();
        await running.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(0, lease.CompleteCalls);
        Assert.Equal(0, lease.RetryCalls);
        Assert.True(processor.CancellationObserved);
    }

    [Fact]
    public async Task WorkerDoesNotRetryWhenCompletionOutcomeThrows()
    {
        var channel = new MessageDeliveryChannel(new WorkQueueOptions { RecoveryHandoffCapacity = 2 });
        var lease = new RecordingLease { CompleteException = new TimeoutException("completion timed out") };
        await using (var reservation = await channel.ReserveAsync(TestContext.Current.CancellationToken))
        {
            reservation.Publish(new WorkDelivery<MessageWorkItem>(
                TestData.Message(),
                "entry-4",
                1,
                DateTimeOffset.UtcNow,
                lease));
        }

        var processor = new RecordingProcessor();
        var worker = CreateWorker(channel, processor);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var running = worker.RunSupervisedAsync("message-1", cancellation.Token);
        await processor.Processed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await lease.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cancellation.Cancel();
        channel.Complete();
        await running.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(1, lease.CompleteCalls);
        Assert.Equal(0, lease.RetryCalls);
    }

    [Fact]
    public async Task WorkerTreatsUnknownCompletionAsUnknownAndDoesNotRetry()
    {
        var channel = new MessageDeliveryChannel(new WorkQueueOptions { RecoveryHandoffCapacity = 2 });
        var lease = new RecordingLease { CompleteResult = LeaseOperationResult.OutcomeUnknown };
        await using (var reservation = await channel.ReserveAsync(TestContext.Current.CancellationToken))
        {
            reservation.Publish(new WorkDelivery<MessageWorkItem>(
                TestData.Message(),
                "entry-5",
                1,
                DateTimeOffset.UtcNow,
                lease));
        }

        var processor = new RecordingProcessor();
        var worker = CreateWorker(channel, processor);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var running = worker.RunSupervisedAsync("message-1", cancellation.Token);
        await lease.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cancellation.Cancel();
        channel.Complete();
        await running.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(1, processor.Calls);
        Assert.Equal(1, lease.CompleteCalls);
        Assert.Equal(0, lease.RetryCalls);
    }

    [Fact]
    public async Task ProcessingTimeoutCancelsHandlerAndLeavesLeasePending()
    {
        using var metrics = new SaucyBotMetrics();
        using var listener = new MeterListener();
        long overdueCount = 0;
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (ReferenceEquals(instrument, metrics.HandlerOverdue))
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, _, _) => overdueCount += measurement);
        listener.Start();

        var channel = new MessageDeliveryChannel(new WorkQueueOptions { RecoveryHandoffCapacity = 2 });
        var lease = new RecordingLease();
        await using (var reservation = await channel.ReserveAsync(TestContext.Current.CancellationToken))
        {
            reservation.Publish(new WorkDelivery<MessageWorkItem>(
                TestData.Message(),
                "entry-6",
                1,
                DateTimeOffset.UtcNow,
                lease));
        }

        var processor = new BlockingProcessor();
        var worker = new MessageQueueWorker(
            channel,
            new QueueMiddlewarePipeline<MessageWorkItem>([]),
            processor,
            new WorkQueueOptions { MaxProcessingTime = TimeSpan.FromMilliseconds(20) },
            NullLogger<MessageQueueWorker>.Instance,
            metrics);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var running = worker.RunSupervisedAsync("message-1", cancellation.Token);
        await processor.CancellationObservedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await lease.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cancellation.Cancel();
        channel.Complete();
        await running.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(0, lease.CompleteCalls);
        Assert.Equal(0, lease.RetryCalls);
        Assert.Equal(0, overdueCount);
    }

    [Fact]
    public async Task SupervisedWorkerRestartsAfterUnexpectedFailureAndStopsOnCancellation()
    {
        using var metrics = new SaucyBotMetrics();
        using var listener = new MeterListener();
        var restartCount = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (ReferenceEquals(instrument, metrics.WorkerRestarts))
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, _, _) => restartCount.TrySetResult(measurement));
        listener.Start();

        var channel = new MessageDeliveryChannel(new WorkQueueOptions { RecoveryHandoffCapacity = 2 });
        channel.Complete(new InvalidOperationException("channel failed"));
        var worker = new MessageQueueWorker(
            channel,
            new QueueMiddlewarePipeline<MessageWorkItem>([]),
            new RecordingProcessor(),
            new WorkQueueOptions(),
            NullLogger<MessageQueueWorker>.Instance,
            metrics);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var running = worker.RunSupervisedAsync("message-1", cancellation.Token);
        Assert.Equal(1, await restartCount.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        cancellation.Cancel();
        await running.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task HandlerOverdueCountsOnlyWhenHandlerRemainsActiveAfterDeadline()
    {
        using var metrics = new SaucyBotMetrics();
        using var listener = new MeterListener();
        var overdueSignal = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (ReferenceEquals(instrument, metrics.HandlerOverdue))
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, _, _) => overdueSignal.TrySetResult(measurement));
        listener.Start();

        var channel = new MessageDeliveryChannel(new WorkQueueOptions { RecoveryHandoffCapacity = 2 });
        var lease = new RecordingLease();
        await using (var reservation = await channel.ReserveAsync(TestContext.Current.CancellationToken))
        {
            reservation.Publish(new WorkDelivery<MessageWorkItem>(
                TestData.Message(),
                "entry-overdue",
                1,
                DateTimeOffset.UtcNow,
                lease));
        }

        var processor = new NonCooperativeProcessor();
        var logger = new RecordingLogger<MessageQueueWorker>();
        var worker = new MessageQueueWorker(
            channel,
            new QueueMiddlewarePipeline<MessageWorkItem>(
                [new QueueMetricsMiddleware<MessageWorkItem>(metrics)]),
            processor,
            new WorkQueueOptions { MaxProcessingTime = TimeSpan.FromMilliseconds(20) },
            logger,
            metrics);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var running = worker.RunSupervisedAsync("message-1", cancellation.Token);

        await processor.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(1, await overdueSignal.Task.WaitAsync(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken));
        Assert.Equal(0, lease.CompleteCalls);

        processor.Release();
        await lease.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cancellation.Cancel();
        channel.Complete();
        await running.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(0, lease.CompleteCalls);
        Assert.Equal(0, lease.RetryCalls);
        Assert.Contains(logger.Messages, message => message.Contains("processing deadline", StringComparison.Ordinal));
    }

    private static MessageQueueWorker CreateWorker(
        MessageDeliveryChannel channel,
        IWorkItemProcessor processor) =>
        new(
            channel,
            new QueueMiddlewarePipeline<MessageWorkItem>([]),
            processor,
            new WorkQueueOptions { MaxProcessingTime = TimeSpan.FromSeconds(10) },
            NullLogger<MessageQueueWorker>.Instance,
            new SaucyBotMetrics());

    private sealed class RecordingProcessor : IWorkItemProcessor
    {
        public MessageWorkItem? Item { get; private set; }
        public int Calls { get; private set; }
        public Exception? Failure { get; init; }
        public TaskCompletionSource Processed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ProcessAsync(MessageWorkItem item, CancellationToken cancellationToken)
        {
            Calls++;
            Item = item;
            if (Failure is not null)
            {
                return Task.FromException(Failure);
            }

            Processed.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingProcessor : IWorkItemProcessor
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancellationObservedSignal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CancellationObserved { get; private set; }

        public async Task ProcessAsync(MessageWorkItem item, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                CancellationObservedSignal.TrySetResult();
                throw;
            }
        }
    }

    private sealed class NonCooperativeProcessor : IWorkItemProcessor
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task ProcessAsync(MessageWorkItem item, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await _release.Task;
        }

        public void Release() => _release.TrySetResult();
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }

    private sealed class RecordingLease : IWorkItemLease
    {
        private readonly CancellationTokenSource _lost = new();
        public CancellationToken LostToken => _lost.Token;
        public int CompleteCalls { get; private set; }
        public int RetryCalls { get; private set; }
        public Exception? CompleteException { get; init; }
        public LeaseOperationResult CompleteResult { get; init; } = LeaseOperationResult.Applied;
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Retried { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<LeaseOperationResult> CompleteAsync(CancellationToken cancellationToken)
        {
            CompleteCalls++;
            Completed.TrySetResult();
            return CompleteException is null
                ? Task.FromResult(CompleteResult)
                : Task.FromException<LeaseOperationResult>(CompleteException);
        }

        public Task<LeaseOperationResult> RetryAsync(Exception exception, CancellationToken cancellationToken)
        {
            RetryCalls++;
            Retried.TrySetResult();
            return Task.FromResult(LeaseOperationResult.Applied);
        }

        public ValueTask DisposeAsync()
        {
            Disposed.TrySetResult();
            _lost.Dispose();
            return ValueTask.CompletedTask;
        }

        public void Lose() => _lost.Cancel();
    }
}
