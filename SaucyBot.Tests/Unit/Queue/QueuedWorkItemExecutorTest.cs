using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
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
        using var listener = Listen(metrics, out var measurements);
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

    private sealed class FakeWorkItemLease : IWorkItemLease
    {
        private readonly CancellationTokenSource _lost = new();

        public int CompleteCalls { get; private set; }
        public List<Exception> RetryExceptions { get; } = [];
        public int DisposeCalls { get; private set; }
        public Exception? CompleteException { get; init; }
        public Exception? RetryException { get; init; }

        public CancellationToken LostToken => _lost.Token;

        public void SignalLost() => _lost.Cancel();

        public Task<LeaseOperationResult> CompleteAsync(CancellationToken cancellationToken)
        {
            CompleteCalls++;
            if (CompleteException is not null)
            {
                return Task.FromException<LeaseOperationResult>(CompleteException);
            }

            return Task.FromResult(LeaseOperationResult.Applied);
        }

        public Task<LeaseOperationResult> RetryAsync(Exception exception, CancellationToken cancellationToken)
        {
            RetryExceptions.Add(exception);
            if (RetryException is not null)
            {
                return Task.FromException<LeaseOperationResult>(RetryException);
            }

            return Task.FromResult(LeaseOperationResult.Applied);
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
}
