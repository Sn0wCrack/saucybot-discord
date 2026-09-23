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

public sealed class QueuedWorkItemExecutorTest
{
    [Fact]
    public async Task LongProcessingRenewsLeaseAndCompletesOnlyAfterHeartbeatStops()
    {
        var redis = new FakeRedisStreamClient();
        var queue = new FakeWorkQueue();
        var processor = new BlockingProcessor();
        var executor = CreateExecutor(redis, queue, processor);
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
    }

    [Fact]
    public async Task HeartbeatFailureCancelsProcessingAndDoesNotCompleteItem()
    {
        var redis = new FakeRedisStreamClient { RenewResult = false };
        var queue = new FakeWorkQueue();
        var processor = new BlockingProcessor();
        var executor = CreateExecutor(redis, queue, processor);
        var item = new QueuedMessageWorkItem("42-0", TestData.Queued().Item);

        await executor.ExecuteAsync("worker-1", item, CancellationToken.None);

        Assert.True(processor.CancellationObserved);
        Assert.Empty(queue.Completed);
    }

    [Fact]
    public async Task ProcessingCancellationDoesNotCompleteItem()
    {
        var redis = new FakeRedisStreamClient();
        var queue = new FakeWorkQueue();
        var processor = new BlockingProcessor();
        var executor = CreateExecutor(redis, queue, processor);
        var item = new QueuedMessageWorkItem("42-0", TestData.Queued().Item);
        using var cancellation = new CancellationTokenSource();

        var execution = executor.ExecuteAsync("worker-1", item, cancellation.Token);
        await processor.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        await execution;

        Assert.True(processor.CancellationObserved);
        Assert.Empty(queue.Completed);
    }

    private static QueuedWorkItemExecutor CreateExecutor(
        FakeRedisStreamClient redis,
        FakeWorkQueue queue,
        BlockingProcessor processor) =>
        new(
            processor,
            queue,
            redis,
            new WorkQueueOptions
            {
                MaxProcessingTime = System.TimeSpan.FromSeconds(5),
                Redis = new()
                {
                    HeartbeatInterval = System.TimeSpan.FromMilliseconds(10),
                },
            },
            NullLogger<QueuedWorkItemExecutor>.Instance,
            new SaucyBotMetrics());

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

    private sealed class FakeWorkQueue : IMessageWorkQueue
    {
        public List<QueuedMessageWorkItem> Completed { get; } = [];
        public Action? OnComplete { get; set; }

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
            CancellationToken cancellationToken) =>
            Task.FromResult(new WorkItemFailureResult(WorkItemFailureAction.Retried, item.DeliveryCount));
    }

    private sealed class FakeRedisStreamClient : IRedisStreamClient
    {
        public bool RenewResult { get; init; } = true;
        public int RenewCalls { get; private set; }
        public TaskCompletionSource Renewed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task EnsureGroupAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<string> AddAsync(string payload, CancellationToken cancellationToken) => Task.FromResult("1-0");
        public Task<RedisStreamEntry?> ReadNewAsync(string consumer, CancellationToken cancellationToken) => Task.FromResult<RedisStreamEntry?>(null);

        public Task<bool> RenewAsync(string consumer, string entryId, CancellationToken cancellationToken)
        {
            RenewCalls++;
            Renewed.TrySetResult();
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
