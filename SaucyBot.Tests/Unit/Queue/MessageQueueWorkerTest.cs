using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
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
        var lease = Substitute.For<IWorkItemLease>();
        lease.LostToken.Returns(CancellationToken.None);
        lease.CompleteAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(LeaseOperationResult.Applied));
        lease.RetryAsync(Arg.Any<Exception>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(LeaseOperationResult.Applied));
        lease.DisposeAsync().Returns(ValueTask.CompletedTask);
        var item = TestData.Message();
        await using (var reservation = await channel.ReserveAsync(TestContext.Current.CancellationToken))
        {
            reservation.Publish(new WorkDelivery<MessageWorkItem>(
                item,
                "entry-1",
                1,
                DateTimeOffset.UtcNow,
                lease));
        }

        var processor = Substitute.For<IWorkItemProcessor>();
        MessageWorkItem? processedItem = null;
        processor.ProcessAsync(Arg.Any<MessageWorkItem>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                processedItem = call.Arg<MessageWorkItem>();
                return Task.CompletedTask;
            });
        var logger = new TestLogger<MessageQueueWorker>();
        var pipeline = new QueueMiddlewarePipeline<MessageWorkItem>(
            [new QueueMetricsMiddleware<MessageWorkItem>(metrics)]);
        var worker = new MessageQueueWorker(
            channel,
            pipeline,
            processor,
            new WorkQueueOptions { MaxProcessingTime = TimeSpan.FromSeconds(10) },
            logger,
            metrics);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var running = worker.RunSupervisedAsync("message-1", cancellation.Token);
        await processor.Received(1).ProcessAsync(item, Arg.Any<CancellationToken>());
        await lease.Received(1).CompleteAsync(Arg.Any<CancellationToken>());
        cancellation.Cancel();
        channel.Complete();
        await running.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Same(item, processedItem);
        await lease.DidNotReceive().RetryAsync(Arg.Any<Exception>(), Arg.Any<CancellationToken>());
        Assert.Equal(1, handlerOutcomes);
        Assert.Equal(0, legacySuccesses);
        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Debug && entry.Message.Contains("started message delivery", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Debug && entry.Message.Contains("completed message delivery", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task WorkerRetriesHandlerFailureThroughTheLease()
    {
        var channel = new MessageDeliveryChannel(new WorkQueueOptions { RecoveryHandoffCapacity = 2 });
        var lease = Substitute.For<IWorkItemLease>();
        lease.LostToken.Returns(CancellationToken.None);
        var retryObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lease.RetryAsync(Arg.Any<Exception>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                retryObserved.TrySetResult();
                return Task.FromResult(LeaseOperationResult.Applied);
            });
        lease.CompleteAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(LeaseOperationResult.Applied));
        lease.DisposeAsync().Returns(ValueTask.CompletedTask);
        await using (var reservation = await channel.ReserveAsync(TestContext.Current.CancellationToken))
        {
            reservation.Publish(new WorkDelivery<MessageWorkItem>(
                TestData.Message(),
                "entry-2",
                1,
                DateTimeOffset.UtcNow,
                lease));
        }

        var processor = Substitute.For<IWorkItemProcessor>();
        processor.ProcessAsync(Arg.Any<MessageWorkItem>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("handler failed")));
        var logger = new TestLogger<MessageQueueWorker>();
        var worker = new MessageQueueWorker(
            channel,
            new QueueMiddlewarePipeline<MessageWorkItem>([]),
            processor,
            new WorkQueueOptions { MaxProcessingTime = TimeSpan.FromSeconds(10) },
            logger,
            new SaucyBotMetrics());
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var running = worker.RunSupervisedAsync("message-1", cancellation.Token);
        await retryObserved.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cancellation.Cancel();
        channel.Complete();
        await running.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await lease.DidNotReceive().CompleteAsync(Arg.Any<CancellationToken>());
        await lease.Received(1).RetryAsync(Arg.Is<Exception>(exception => exception.Message == "handler failed"), Arg.Any<CancellationToken>());
        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Debug && entry.Message.Contains("handler failed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task WorkerDoesNotCompleteOrRetryAfterLeaseLoss()
    {
        var channel = new MessageDeliveryChannel(new WorkQueueOptions { RecoveryHandoffCapacity = 2 });
        using var lostLease = new CancellationTokenSource();
        var leaseDisposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lease = CreateLease(lostLease);
        lease.DisposeAsync().Returns(_ =>
        {
            leaseDisposed.TrySetResult();
            return ValueTask.CompletedTask;
        });
        await using (var reservation = await channel.ReserveAsync(TestContext.Current.CancellationToken))
        {
            reservation.Publish(new WorkDelivery<MessageWorkItem>(
                TestData.Message(),
                "entry-3",
                1,
                DateTimeOffset.UtcNow,
                lease));
        }

        var processor = Substitute.For<IWorkItemProcessor>();
        var processorStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        processor.ProcessAsync(Arg.Any<MessageWorkItem>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var processingToken = call.Arg<CancellationToken>();
                processorStarted.TrySetResult();
                processingToken.Register(() => cancellationObserved.TrySetResult());
                return Task.Delay(Timeout.InfiniteTimeSpan, processingToken);
            });
        var worker = new MessageQueueWorker(
            channel,
            new QueueMiddlewarePipeline<MessageWorkItem>([]),
            processor,
            new WorkQueueOptions { MaxProcessingTime = TimeSpan.FromSeconds(10) },
            NullLogger<MessageQueueWorker>.Instance,
            new SaucyBotMetrics());
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var running = worker.RunSupervisedAsync("message-1", cancellation.Token);
        await processorStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        lostLease.Cancel();
        await leaseDisposed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cancellation.Cancel();
        channel.Complete();
        await running.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await lease.DidNotReceive().CompleteAsync(Arg.Any<CancellationToken>());
        await lease.DidNotReceive().RetryAsync(Arg.Any<Exception>(), Arg.Any<CancellationToken>());
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WorkerDoesNotRetryWhenCompletionOutcomeThrows()
    {
        var channel = new MessageDeliveryChannel(new WorkQueueOptions { RecoveryHandoffCapacity = 2 });
        var completionSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lease = CreateLease();
        lease.CompleteAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            completionSignal.TrySetResult();
            return Task.FromException<LeaseOperationResult>(new TimeoutException("completion timed out"));
        });
        await using (var reservation = await channel.ReserveAsync(TestContext.Current.CancellationToken))
        {
            reservation.Publish(new WorkDelivery<MessageWorkItem>(
                TestData.Message(),
                "entry-4",
                1,
                DateTimeOffset.UtcNow,
                lease));
        }

        var processor = Substitute.For<IWorkItemProcessor>();
        processor.ProcessAsync(Arg.Any<MessageWorkItem>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var worker = CreateWorker(channel, processor);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var running = worker.RunSupervisedAsync("message-1", cancellation.Token);
        await completionSignal.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cancellation.Cancel();
        channel.Complete();
        await running.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await lease.Received(1).CompleteAsync(CancellationToken.None);
        await lease.DidNotReceive().RetryAsync(Arg.Any<Exception>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WorkerTreatsUnknownCompletionAsUnknownAndDoesNotRetry()
    {
        var channel = new MessageDeliveryChannel(new WorkQueueOptions { RecoveryHandoffCapacity = 2 });
        var completionSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lease = CreateLease();
        lease.CompleteAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            completionSignal.TrySetResult();
            return Task.FromResult(LeaseOperationResult.OutcomeUnknown);
        });
        await using (var reservation = await channel.ReserveAsync(TestContext.Current.CancellationToken))
        {
            reservation.Publish(new WorkDelivery<MessageWorkItem>(
                TestData.Message(),
                "entry-5",
                1,
                DateTimeOffset.UtcNow,
                lease));
        }

        var processor = Substitute.For<IWorkItemProcessor>();
        processor.ProcessAsync(Arg.Any<MessageWorkItem>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var worker = CreateWorker(channel, processor);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var running = worker.RunSupervisedAsync("message-1", cancellation.Token);
        await completionSignal.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cancellation.Cancel();
        channel.Complete();
        await running.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await processor.Received(1).ProcessAsync(Arg.Any<MessageWorkItem>(), Arg.Any<CancellationToken>());
        await lease.Received(1).CompleteAsync(Arg.Any<CancellationToken>());
        await lease.DidNotReceive().RetryAsync(Arg.Any<Exception>(), Arg.Any<CancellationToken>());
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
        var leaseDisposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lease = CreateLease();
        lease.DisposeAsync().Returns(_ =>
        {
            leaseDisposed.TrySetResult();
            return ValueTask.CompletedTask;
        });
        await using (var reservation = await channel.ReserveAsync(TestContext.Current.CancellationToken))
        {
            reservation.Publish(new WorkDelivery<MessageWorkItem>(
                TestData.Message(),
                "entry-6",
                1,
                DateTimeOffset.UtcNow,
                lease));
        }

        var processor = Substitute.For<IWorkItemProcessor>();
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        processor.ProcessAsync(Arg.Any<MessageWorkItem>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var processingToken = call.Arg<CancellationToken>();
                processingToken.Register(() => cancellationObserved.TrySetResult());
                return Task.Delay(Timeout.InfiniteTimeSpan, processingToken);
            });
        var worker = new MessageQueueWorker(
            channel,
            new QueueMiddlewarePipeline<MessageWorkItem>([]),
            processor,
            new WorkQueueOptions { MaxProcessingTime = TimeSpan.FromMilliseconds(20) },
            NullLogger<MessageQueueWorker>.Instance,
            metrics);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var running = worker.RunSupervisedAsync("message-1", cancellation.Token);
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await leaseDisposed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cancellation.Cancel();
        channel.Complete();
        await running.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await lease.DidNotReceive().CompleteAsync(Arg.Any<CancellationToken>());
        await lease.DidNotReceive().RetryAsync(Arg.Any<Exception>(), Arg.Any<CancellationToken>());
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
            Substitute.For<IWorkItemProcessor>(),
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
        var leaseDisposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lease = CreateLease();
        lease.DisposeAsync().Returns(_ =>
        {
            leaseDisposed.TrySetResult();
            return ValueTask.CompletedTask;
        });
        await using (var reservation = await channel.ReserveAsync(TestContext.Current.CancellationToken))
        {
            reservation.Publish(new WorkDelivery<MessageWorkItem>(
                TestData.Message(),
                "entry-overdue",
                1,
                DateTimeOffset.UtcNow,
                lease));
        }

        var processor = Substitute.For<IWorkItemProcessor>();
        var processorStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProcessor = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        processor.ProcessAsync(Arg.Any<MessageWorkItem>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                processorStarted.TrySetResult();
                return releaseProcessor.Task;
            });
        var logger = new TestLogger<MessageQueueWorker>();
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

        await processorStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(1, await overdueSignal.Task.WaitAsync(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken));
        await lease.DidNotReceive().CompleteAsync(Arg.Any<CancellationToken>());

        releaseProcessor.TrySetResult();
        await leaseDisposed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cancellation.Cancel();
        channel.Complete();
        await running.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await lease.DidNotReceive().CompleteAsync(Arg.Any<CancellationToken>());
        await lease.DidNotReceive().RetryAsync(Arg.Any<Exception>(), Arg.Any<CancellationToken>());
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

    private static IWorkItemLease CreateLease(CancellationTokenSource? lostTokenSource = null)
    {
        var lease = Substitute.For<IWorkItemLease>();
        lease.LostToken.Returns(lostTokenSource?.Token ?? CancellationToken.None);
        lease.CompleteAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(LeaseOperationResult.Applied));
        lease.RetryAsync(Arg.Any<Exception>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(LeaseOperationResult.Applied));
        lease.DisposeAsync().Returns(ValueTask.CompletedTask);
        return lease;
    }
}
