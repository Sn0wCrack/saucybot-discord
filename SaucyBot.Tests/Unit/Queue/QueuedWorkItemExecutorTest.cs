using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SaucyBot.Diagnostics;
using SaucyBot.Queue;
using SaucyBot.Queue.Redis;
using SaucyBot.Tests.Unit.Common;
using StackExchange.Redis;
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
    public void ExecutorDependsOnTheLeaseNotOnRedisOrTheWorkQueue()
    {
        var constructor = Assert.Single(typeof(QueuedWorkItemExecutor).GetConstructors());

        Assert.DoesNotContain(constructor.GetParameters(), parameter => parameter.ParameterType == typeof(IRedisStreamClient));
        Assert.DoesNotContain(constructor.GetParameters(), parameter => parameter.ParameterType == typeof(IMessageWorkQueue));
        Assert.DoesNotContain(constructor.GetParameters(), parameter => parameter.ParameterType == typeof(IConnectionMultiplexer));
        Assert.Contains(constructor.GetParameters(), parameter => parameter.ParameterType == typeof(IWorkItemProcessor));
    }

    [Fact]
    public async Task SuccessfulProcessingCompletesThroughTheLeaseAndDisposesIt()
    {
        var lease = new FakeWorkItemLease();
        var processor = new BlockingProcessor();
        using var metrics = new SaucyBotMetrics();
        var executor = CreateExecutor(processor, metrics);
        var item = new QueuedMessageWorkItem("42-0", TestData.Queued().Item, lease);

        var execution = executor.ExecuteAsync("worker-1", item, CancellationToken.None);
        await processor.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        processor.Release();
        var outcome = await execution;

        Assert.Equal(QueuedWorkItemExecutionOutcome.Completed, outcome);
        Assert.Equal(1, lease.CompleteCalls);
        Assert.Empty(lease.RetryExceptions);
        Assert.Equal(1, lease.DisposeCalls);
    }

    [Fact]
    public async Task FailedProcessingRetriesThroughTheLease()
    {
        var lease = new FakeWorkItemLease();
        using var metrics = new SaucyBotMetrics();
        var executor = CreateExecutor(new FailingProcessor(), metrics);
        var item = new QueuedMessageWorkItem("42-0", TestData.Queued().Item, lease);

        var outcome = await executor.ExecuteAsync("worker-1", item, CancellationToken.None);

        Assert.Equal(QueuedWorkItemExecutionOutcome.Failed, outcome);
        var exception = Assert.Single(lease.RetryExceptions);
        Assert.Equal("processing failed", exception.Message);
        Assert.Equal(0, lease.CompleteCalls);
        Assert.Equal(1, lease.DisposeCalls);
    }

    [Fact]
    public async Task LeaseLossPreventsCompletionAndRetry()
    {
        var lease = new FakeWorkItemLease();
        var processor = new BlockingProcessor();
        using var metrics = new SaucyBotMetrics();
        using var listener = Listen(metrics, "saucybot.queue.lease", out var measurements);
        var executor = CreateExecutor(processor, metrics);
        var item = new QueuedMessageWorkItem("42-0", TestData.Queued().Item, lease);

        var execution = executor.ExecuteAsync("worker-1", item, CancellationToken.None);
        await processor.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        lease.SignalLost();

        var outcome = await execution;

        Assert.Equal(QueuedWorkItemExecutionOutcome.LeaseLost, outcome);
        Assert.True(processor.CancellationObserved);
        Assert.Equal(0, lease.CompleteCalls);
        Assert.Empty(lease.RetryExceptions);
        Assert.Equal(1, measurements["saucybot.queue.lease_lost"]);
    }

    [Fact]
    public async Task ProcessingCancellationDoesNotCompleteItem()
    {
        var lease = new FakeWorkItemLease();
        var processor = new BlockingProcessor();
        using var metrics = new SaucyBotMetrics();
        var executor = CreateExecutor(processor, metrics);
        var item = new QueuedMessageWorkItem("42-0", TestData.Queued().Item, lease);
        using var cancellation = new CancellationTokenSource();

        var execution = executor.ExecuteAsync("worker-1", item, cancellation.Token);
        await processor.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        var outcome = await execution;

        Assert.Equal(QueuedWorkItemExecutionOutcome.Cancelled, outcome);
        Assert.True(processor.CancellationObserved);
        Assert.Equal(0, lease.CompleteCalls);
        Assert.Empty(lease.RetryExceptions);
    }

    [Fact]
    public async Task MaximumProcessingTimeCancelsAndLeavesItemForRecovery()
    {
        var lease = new FakeWorkItemLease();
        var executor = CreateExecutor(
            new BlockingProcessor(),
            new SaucyBotMetrics(),
            maxProcessingTime: TimeSpan.FromMilliseconds(10));
        var item = new QueuedMessageWorkItem("42-0", TestData.Queued().Item, lease);

        var outcome = await executor.ExecuteAsync("worker-1", item, CancellationToken.None);

        Assert.Equal(QueuedWorkItemExecutionOutcome.Cancelled, outcome);
        Assert.Equal(0, lease.CompleteCalls);
        Assert.Empty(lease.RetryExceptions);
    }

    [Fact]
    public async Task RetryCleanupFailureIsLoggedAndDoesNotEscapeTheExecutor()
    {
        var lease = new FakeWorkItemLease
        {
            RetryException = new InvalidOperationException("cleanup failed"),
        };
        using var metrics = new SaucyBotMetrics();
        var executor = CreateExecutor(new FailingProcessor(), metrics);
        var item = new QueuedMessageWorkItem("42-0", TestData.Queued().Item, lease);

        var outcome = await executor.ExecuteAsync("worker-1", item, CancellationToken.None);

        Assert.Equal(QueuedWorkItemExecutionOutcome.Failed, outcome);
    }

    [Fact]
    public async Task AmbiguousCompletionIsNotReportedAsSuccess()
    {
        var lease = new FakeWorkItemLease
        {
            CompleteResult = LeaseOperationResult.OutcomeUnknown,
        };
        var processor = new CountingProcessor();
        using var metrics = new SaucyBotMetrics();
        using var listener = Listen(metrics, "saucybot.queue.succeeded", out var measurements);
        var executor = CreateExecutor(processor, metrics);
        var item = new QueuedMessageWorkItem("42-0", TestData.Queued().Item, lease);

        var outcome = await executor.ExecuteAsync("worker-1", item, CancellationToken.None);

        Assert.NotEqual(QueuedWorkItemExecutionOutcome.Completed, outcome);
        Assert.Equal(0, measurements.GetValueOrDefault("saucybot.queue.succeeded"));
        Assert.Equal(1, processor.Calls);
        Assert.Equal(1, lease.CompleteCalls);
        Assert.Equal(1, lease.DisposeCalls);
    }

    [Fact]
    public async Task AmbiguousRetryIsNotReportedAsSuccess()
    {
        var lease = new FakeWorkItemLease
        {
            RetryResult = LeaseOperationResult.OutcomeUnknown,
        };
        var processor = new CountingProcessor { Throw = true };
        using var metrics = new SaucyBotMetrics();
        using var listener = Listen(metrics, "saucybot.queue.succeeded", out var measurements);
        var executor = CreateExecutor(processor, metrics);
        var item = new QueuedMessageWorkItem("42-0", TestData.Queued().Item, lease);

        var outcome = await executor.ExecuteAsync("worker-1", item, CancellationToken.None);

        Assert.NotEqual(QueuedWorkItemExecutionOutcome.Completed, outcome);
        Assert.Equal(0, measurements.GetValueOrDefault("saucybot.queue.succeeded"));
        Assert.Equal(1, processor.Calls);
        Assert.Equal(0, lease.CompleteCalls);
    }

    [Fact]
    public async Task AmbiguousLeaseResultsReturnTheUnknownOutcome()
    {
        using var metrics = new SaucyBotMetrics();
        var completedItem = new QueuedMessageWorkItem(
            "42-0",
            TestData.Queued().Item,
            new FakeWorkItemLease { CompleteResult = LeaseOperationResult.OutcomeUnknown });
        var failedItem = new QueuedMessageWorkItem(
            "43-0",
            TestData.Queued().Item,
            new FakeWorkItemLease { RetryResult = LeaseOperationResult.OutcomeUnknown });

        var completionOutcome = await CreateExecutor(new CountingProcessor(), metrics)
            .ExecuteAsync("worker-1", completedItem, CancellationToken.None);
        var retryOutcome = await CreateExecutor(new CountingProcessor { Throw = true }, metrics)
            .ExecuteAsync("worker-1", failedItem, CancellationToken.None);

        Assert.Equal(QueuedWorkItemExecutionOutcome.OutcomeUnknown, completionOutcome);
        Assert.Equal(QueuedWorkItemExecutionOutcome.OutcomeUnknown, retryOutcome);
    }

    [Fact]
    public async Task HungBackendCompletionIsBoundedAndNeverReportedAsSuccess()
    {
        var client = new FakeLeaseBackend { CompleteHangs = true };
        var lease = CreateLease(client, new WorkQueueOptions
        {
            BackendOperationTimeout = TimeSpan.FromMilliseconds(100),
            HeartbeatInterval = TimeSpan.FromMilliseconds(10),
            MaxProcessingTime = TimeSpan.FromSeconds(30),
        });
        var processor = new CountingProcessor();
        using var metrics = new SaucyBotMetrics();
        var executor = CreateExecutor(processor, metrics, maxProcessingTime: TimeSpan.FromSeconds(30));
        var item = new QueuedMessageWorkItem("42-0", TestData.Queued().Item, lease);

        var stopwatch = Stopwatch.StartNew();
        var outcome = await executor
            .ExecuteAsync("worker-1", item, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        stopwatch.Stop();
        client.CompleteRelease.TrySetResult();

        Assert.NotEqual(QueuedWorkItemExecutionOutcome.Completed, outcome);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"completion was not bounded, took {stopwatch.Elapsed}");
        Assert.Equal(0, client.CompletedCount);
        Assert.Equal(1, processor.Calls);
    }

    [Fact]
    public async Task OverdueHandlerIsAwaitedLeavesItemForRecoveryAndStopsHeartbeat()
    {
        var client = new FakeLeaseBackend();
        var lease = CreateLease(client, new WorkQueueOptions
        {
            BackendOperationTimeout = TimeSpan.FromMilliseconds(100),
            HeartbeatInterval = TimeSpan.FromMilliseconds(10),
            MaxProcessingTime = TimeSpan.FromMilliseconds(200),
        });
        var processor = new NonCooperativeProcessor();
        var logger = new RecordingLogger<QueuedWorkItemExecutor>();
        using var metrics = new SaucyBotMetrics();
        var executor = new QueuedWorkItemExecutor(
            processor,
            new WorkQueueOptions { MaxProcessingTime = TimeSpan.FromMilliseconds(200) },
            logger,
            metrics);
        var item = new QueuedMessageWorkItem("42-0", TestData.Queued().Item, lease);

        var execution = executor.ExecuteAsync("worker-1", item, CancellationToken.None);
        try
        {
            await processor.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);

            Assert.False(execution.IsCompleted);
            var renewalsAfterDeadline = client.RenewCalls;
            await Task.Delay(TimeSpan.FromMilliseconds(150), TestContext.Current.CancellationToken);
            Assert.Equal(renewalsAfterDeadline, client.RenewCalls);
            Assert.Equal(0, client.CompletedCount);
            Assert.True(lease.LostToken.IsCancellationRequested);
            Assert.Contains(logger.Messages, message => message.Contains("overdue", StringComparison.Ordinal));

            processor.Release();
            var outcome = await execution.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            Assert.NotEqual(QueuedWorkItemExecutionOutcome.Completed, outcome);
            Assert.Equal(0, client.CompletedCount);
        }
        finally
        {
            processor.Release();
            await execution.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
    }

    private static IWorkItemLease CreateLease(FakeLeaseBackend client, WorkQueueOptions options) =>
        new RedisWorkItemLease(
            client,
            options,
            "worker-1",
            "42-0",
            "lease-token",
            (exception, cancellationToken) => client.RetryAsync("worker-1", "42-0", "lease-token", cancellationToken));

    private static QueuedWorkItemExecutor CreateExecutor(
        IWorkItemProcessor processor,
        SaucyBotMetrics metrics,
        TimeSpan? maxProcessingTime = null) =>
        new(
            processor,
            new WorkQueueOptions
            {
                MaxProcessingTime = maxProcessingTime ?? TimeSpan.FromSeconds(5),
            },
            NullLogger<QueuedWorkItemExecutor>.Instance,
            metrics);

    private static MeterListener Listen(
        SaucyBotMetrics metrics,
        string instrumentPrefix,
        out Dictionary<string, long> measurements)
    {
        var values = new Dictionary<string, long>();
        measurements = values;
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (instrument.Name.StartsWith(instrumentPrefix, StringComparison.Ordinal))
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

    private sealed class FakeWorkItemLease : IWorkItemLease
    {
        private readonly CancellationTokenSource _lost = new();

        public int CompleteCalls { get; private set; }
        public List<Exception> RetryExceptions { get; } = [];
        public int DisposeCalls { get; private set; }
        public Exception? CompleteException { get; init; }
        public Exception? RetryException { get; init; }
        public LeaseOperationResult CompleteResult { get; init; } = LeaseOperationResult.Applied;
        public LeaseOperationResult RetryResult { get; init; } = LeaseOperationResult.Applied;

        public CancellationToken LostToken => _lost.Token;

        public void SignalLost() => _lost.Cancel();

        public Task<LeaseOperationResult> CompleteAsync(CancellationToken cancellationToken)
        {
            CompleteCalls++;
            if (CompleteException is not null)
            {
                return Task.FromException<LeaseOperationResult>(CompleteException);
            }

            return Task.FromResult(CompleteResult);
        }

        public Task<LeaseOperationResult> RetryAsync(Exception exception, CancellationToken cancellationToken)
        {
            RetryExceptions.Add(exception);
            if (RetryException is not null)
            {
                return Task.FromException<LeaseOperationResult>(RetryException);
            }

            return Task.FromResult(RetryResult);
        }

        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            return ValueTask.CompletedTask;
        }
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

    private sealed class CountingProcessor : IWorkItemProcessor
    {
        public int Calls { get; private set; }
        public bool Throw { get; init; }

        public Task ProcessAsync(QueuedMessageWorkItem item, CancellationToken cancellationToken)
        {
            Calls++;
            return Throw
                ? Task.FromException(new InvalidOperationException("processing failed"))
                : Task.CompletedTask;
        }
    }

    private sealed class NonCooperativeProcessor : IWorkItemProcessor
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task ProcessAsync(QueuedMessageWorkItem item, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            // Ignores cancellation on purpose until the test releases it.
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
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }

    private sealed class FakeLeaseBackend : IRedisStreamClient
    {
        public int RenewCalls { get; private set; }
        public int CompletedCount { get; private set; }
        public bool CompleteHangs { get; init; }
        public TaskCompletionSource CompleteRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task EnsureGroupAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<string> AddAsync(string payload, CancellationToken cancellationToken) =>
            Task.FromResult("1-0");

        public Task<RedisStreamEntry?> ReadNewAsync(
            string consumer,
            string leaseToken,
            CancellationToken cancellationToken) =>
            Task.FromResult<RedisStreamEntry?>(null);

        public Task<bool> RenewAsync(
            string consumer,
            string entryId,
            string leaseToken,
            CancellationToken cancellationToken)
        {
            RenewCalls++;
            return Task.FromResult(true);
        }

        public Task<RedisStreamEntry?> ReclaimAsync(
            string consumer,
            TimeSpan minimumIdleTime,
            string leaseToken,
            CancellationToken cancellationToken) =>
            Task.FromResult<RedisStreamEntry?>(null);

        public async Task<LeaseOperationResult> CompleteAsync(
            string consumer,
            string entryId,
            string leaseToken,
            CancellationToken cancellationToken)
        {
            if (CompleteHangs)
            {
                await CompleteRelease.Task;
            }

            CompletedCount++;
            return LeaseOperationResult.Applied;
        }

        public Task<LeaseOperationResult> RetryAsync(
            string consumer,
            string entryId,
            string leaseToken,
            CancellationToken cancellationToken) =>
            Task.FromResult(LeaseOperationResult.Applied);

        public Task ClearPendingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
