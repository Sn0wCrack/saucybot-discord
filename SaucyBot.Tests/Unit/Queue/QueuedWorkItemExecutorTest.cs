using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SaucyBot.Diagnostics;
using SaucyBot.Queue;
using SaucyBot.Queue.Redis;
using SaucyBot.Tests.Unit.Common;
using Xunit;

namespace SaucyBot.Tests.Unit.Queue;

public sealed class QueuedWorkItemExecutorTest
{
    [Fact]
    public void MetricsExposeLeaseAndRecoveryInstruments()
    {
        using var metrics = new SaucyBotMetrics();

        Assert.NotNull(metrics.LeaseRenewed);
        Assert.NotNull(metrics.LeaseLost);
        Assert.NotNull(metrics.Reclaimed);
        Assert.NotNull(metrics.WorkerRestarts);
    }

    [Fact]
    public async Task LongProcessingRenewsLeaseAndCompletesOnlyAfterHeartbeatStops()
    {
        var redis = new FakeRedisStreamClient();
        var queue = new FakeWorkQueue();
        var processor = new BlockingProcessor();
        using var metrics = new SaucyBotMetrics();
        using var listener = Listen(metrics, out var measurements);
        var executor = CreateExecutor(redis, queue, processor, metrics);
        var item = new QueuedMessageWorkItem("42-0", TestData.Queued().Item);
        var renewalsAtCompletion = -1;
        queue.OnComplete = () => renewalsAtCompletion = redis.RenewCalls;

        var execution = executor.ExecuteAsync("worker-1", item, CancellationToken.None);
        await processor.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        await redis.Renewed.Task.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Empty(queue.Completed);

        processor.Release();
        await execution;

        Assert.Single(queue.Completed);
        Assert.True(redis.RenewCalls > 0);
        Assert.Equal(redis.RenewCalls, renewalsAtCompletion);
        Assert.True(measurements["saucybot.queue.lease_renewed"] > 0);
    }

    [Fact]
    public async Task HeartbeatFailureCancelsProcessingAndDoesNotCompleteItem()
    {
        var redis = new FakeRedisStreamClient { RenewResult = false };
        var queue = new FakeWorkQueue();
        var processor = new BlockingProcessor();
        using var metrics = new SaucyBotMetrics();
        using var listener = Listen(metrics, out var measurements);
        var executor = CreateExecutor(redis, queue, processor, metrics);
        var item = new QueuedMessageWorkItem("42-0", TestData.Queued().Item);

        await executor.ExecuteAsync("worker-1", item, CancellationToken.None);

        Assert.True(processor.CancellationObserved);
        Assert.Empty(queue.Completed);
        Assert.Equal(1, measurements["saucybot.queue.lease_lost"]);
    }

    [Fact]
    public async Task HeartbeatExceptionCancelsProcessingAndDoesNotCompleteItem()
    {
        var redis = new FakeRedisStreamClient
        {
            RenewException = new InvalidOperationException("redis unavailable"),
        };
        var queue = new FakeWorkQueue();
        var processor = new BlockingProcessor();
        var executor = CreateExecutor(redis, queue, processor);
        var item = new QueuedMessageWorkItem("42-0", TestData.Queued().Item);

        var outcome = await executor.ExecuteAsync("worker-1", item, CancellationToken.None);

        Assert.Equal(QueuedWorkItemExecutionOutcome.LeaseLost, outcome);
        Assert.True(processor.CancellationObserved);
        Assert.Empty(queue.Completed);
    }

    [Fact]
    public async Task ProcessingCancellationDoesNotCompleteItem()
    {
        var redis = new FakeRedisStreamClient();
        var queue = new FakeWorkQueue();
        var processor = new BlockingProcessor();
        using var metrics = new SaucyBotMetrics();
        var executor = CreateExecutor(redis, queue, processor, metrics);
        var item = new QueuedMessageWorkItem("42-0", TestData.Queued().Item);
        using var cancellation = new CancellationTokenSource();

        var execution = executor.ExecuteAsync("worker-1", item, cancellation.Token);
        await processor.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        await execution;

        Assert.True(processor.CancellationObserved);
        Assert.Empty(queue.Completed);
    }

    [Fact]
    public async Task MaximumProcessingTimeCancelsAndLeavesItemForRecovery()
    {
        var redis = new FakeRedisStreamClient();
        var queue = new FakeWorkQueue();
        var processor = new BlockingProcessor();
        var executor = CreateExecutor(
            redis,
            queue,
            processor,
            maxProcessingTime: TimeSpan.FromMilliseconds(10));
        var item = new QueuedMessageWorkItem("42-0", TestData.Queued().Item);

        var outcome = await executor.ExecuteAsync("worker-1", item, CancellationToken.None);

        Assert.Equal(QueuedWorkItemExecutionOutcome.Cancelled, outcome);
        Assert.True(processor.CancellationObserved);
        Assert.Empty(queue.Completed);
    }

    [Fact]
    public async Task FailureCleanupFailureIsLoggedAndDoesNotEscapeTheExecutor()
    {
        var redis = new FakeRedisStreamClient();
        var queue = new FakeWorkQueue
        {
            FailureException = new InvalidOperationException("cleanup failed"),
        };
        var executor = CreateExecutor(redis, queue, new FailingProcessor());
        var item = new QueuedMessageWorkItem("42-0", TestData.Queued().Item);

        var outcome = await executor.ExecuteAsync("worker-1", item, CancellationToken.None);

        Assert.Equal(QueuedWorkItemExecutionOutcome.Failed, outcome);
    }

    private static QueuedWorkItemExecutor CreateExecutor(
        FakeRedisStreamClient redis,
        FakeWorkQueue queue,
        IWorkItemProcessor processor,
        SaucyBotMetrics? metrics = null,
        TimeSpan? maxProcessingTime = null) =>
        new(
            processor,
            queue,
            redis,
            new WorkQueueOptions
            {
                MaxProcessingTime = maxProcessingTime ?? System.TimeSpan.FromSeconds(5),
                HeartbeatInterval = System.TimeSpan.FromMilliseconds(10),
            },
            NullLogger<QueuedWorkItemExecutor>.Instance,
            metrics ?? new SaucyBotMetrics());

    private static MeterListener Listen(
        SaucyBotMetrics metrics,
        out Dictionary<string, long> measurements)
    {
        var values = new Dictionary<string, long>();
        measurements = values;
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (instrument.Name.StartsWith("saucybot.queue.lease", StringComparison.Ordinal))
            {
                current.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, _, _) =>
        {
            values[instrument.Name] =
                values.GetValueOrDefault(instrument.Name) + measurement;
        });
        listener.Start();
        return listener;
    }

    private sealed class BlockingProcessor : IWorkItemProcessor
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CancellationObserved { get; private set; }
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task ProcessAsync(QueuedMessageWorkItem item, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try
            {
                await _release.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }
        }

        public void Release() => _release.TrySetResult();
    }

    private sealed class FailingProcessor : IWorkItemProcessor
    {
        public Task ProcessAsync(QueuedMessageWorkItem item, CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException("processing failed"));
    }

    private sealed class FakeWorkQueue : IMessageWorkQueue
    {
        public List<QueuedMessageWorkItem> Completed { get; } = [];
        public Action? OnComplete { get; set; }
        public Exception? FailureException { get; init; }

        public Task EnqueueAsync(MessageWorkItem item, CancellationToken cancellationToken) =>
            throw new System.NotSupportedException();

        public async IAsyncEnumerable<QueuedMessageWorkItem> ReadAsync(
            string consumer,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public async IAsyncEnumerable<QueuedMessageWorkItem> ReclaimAsync(
            string consumer,
            System.TimeSpan minimumIdleTime,
            int count,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task CompleteAsync(QueuedMessageWorkItem item, CancellationToken cancellationToken)
        {
            OnComplete?.Invoke();
            Completed.Add(item);
            return Task.CompletedTask;
        }

        public Task<WorkItemFailureResult> FailAsync(
            QueuedMessageWorkItem item,
            System.Exception exception,
            CancellationToken cancellationToken)
        {
            if (FailureException is not null)
            {
                return Task.FromException<WorkItemFailureResult>(FailureException);
            }

            return Task.FromResult(new WorkItemFailureResult(WorkItemFailureAction.Retried, item.DeliveryCount));
        }
    }

    private sealed class FakeRedisStreamClient : IRedisStreamClient
    {
        public bool RenewResult { get; init; } = true;
        public Exception? RenewException { get; init; }
        public int RenewCalls { get; private set; }
        public TaskCompletionSource Renewed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task EnsureGroupAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<string> AddAsync(string payload, CancellationToken cancellationToken) => Task.FromResult("1-0");
        public Task<RedisStreamEntry?> ReadNewAsync(string consumer, CancellationToken cancellationToken) => Task.FromResult<RedisStreamEntry?>(null);

        public Task<bool> RenewAsync(string consumer, string entryId, CancellationToken cancellationToken)
        {
            RenewCalls++;
            Renewed.TrySetResult();
            if (RenewException is not null)
            {
                return Task.FromException<bool>(RenewException);
            }

            return Task.FromResult(RenewResult);
        }

        public Task<IReadOnlyList<RedisStreamEntry>> ReclaimAsync(
            string consumer,
            System.TimeSpan minimumIdleTime,
            int count,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RedisStreamEntry>>([]);

        public Task AcknowledgeAsync(string entryId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteAsync(string entryId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ClearPendingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
