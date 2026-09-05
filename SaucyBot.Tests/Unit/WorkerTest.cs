using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SaucyBot;
using SaucyBot.Diagnostics;
using SaucyBot.Queue;
using SaucyBot.Services;
using Xunit;

namespace SaucyBot.Tests.Unit;

public sealed class WorkerTest
{
    [Fact]
    public async Task WorkersProcessItemsAndAcknowledgeOnlyAfterSuccessfulProcessing()
    {
        var queue = new FakeWorkQueue();
        var processor = new RecordingProcessor();
        var item = CreateQueuedItem("1-0");
        queue.Add(item);

        await using var service = new WorkQueueHostedService(
            queue,
            processor,
            new WorkQueueOptions { MessageWorkerCount = 1 },
            SubstituteLogger<WorkQueueHostedService>());

        await service.StartAsync(TestContext.Current.CancellationToken);
        await processor.Processed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal([item], processor.Items);
        Assert.Equal([item], queue.Acknowledged);
    }

    [Fact]
    public async Task WorkerExceptionsAreObservedAndDoNotStopOtherWorkers()
    {
        var queue = new FakeWorkQueue();
        var processor = new RecordingProcessor { ThrowOnFirstItem = true };
        var failed = CreateQueuedItem("1-0");
        var succeeded = CreateQueuedItem("2-0");
        queue.Add(failed);
        queue.Add(succeeded);

        await using var service = new WorkQueueHostedService(
            queue,
            processor,
            new WorkQueueOptions { MessageWorkerCount = 1 },
            SubstituteLogger<WorkQueueHostedService>());

        await service.StartAsync(TestContext.Current.CancellationToken);
        await processor.Processed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal([failed, succeeded], processor.Items);
        Assert.Equal([succeeded], queue.Acknowledged);
    }

    [Fact]
    public async Task ShutdownCancelsActiveWorkAndStopsReading()
    {
        var queue = new FakeWorkQueue();
        var processor = new RecordingProcessor { Block = true };
        var item = CreateQueuedItem("1-0");
        queue.Add(item);

        await using var service = new WorkQueueHostedService(
            queue,
            processor,
            new WorkQueueOptions
            {
                MessageWorkerCount = 1,
                ShutdownDrainTimeout = TimeSpan.FromMilliseconds(100)
            },
            SubstituteLogger<WorkQueueHostedService>());

        await service.StartAsync(TestContext.Current.CancellationToken);
        await processor.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);
        await processor.CancellationObservedSource.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.True(processor.CancellationObserved);
        Assert.True(queue.ReadCancellationObserved);
        Assert.Empty(queue.Acknowledged);
    }

    [Fact]
    public async Task ShutdownStopsAdmissionBeforeDrainingWorkers()
    {
        var queue = new FakeWorkQueue();
        var processor = new RecordingProcessor { Block = true };
        var interactionChannel = new InteractionWorkChannel(new WorkQueueOptions { InteractionChannelCapacity = 1 });
        await interactionChannel.WriteAsync(null!, CancellationToken.None);

        await using var service = new WorkQueueHostedService(
            queue,
            processor,
            new WorkQueueOptions { MessageWorkerCount = 1, ShutdownDrainTimeout = TimeSpan.FromMilliseconds(100) },
            SubstituteLogger<WorkQueueHostedService>(),
            interactionChannel);

        service.StopIntake();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            interactionChannel.WriteAsync(null!, service.AdmissionToken).AsTask());
    }

    [Fact]
    public async Task WorkersRespectConfiguredConcurrencyLimit()
    {
        var queue = new FakeWorkQueue();
        var processor = new RecordingProcessor { Block = true };
        queue.Add(CreateQueuedItem("1-0"));
        queue.Add(CreateQueuedItem("2-0"));

        await using var service = new WorkQueueHostedService(
            queue,
            processor,
            new WorkQueueOptions { MessageWorkerCount = 2, ShutdownDrainTimeout = TimeSpan.FromMilliseconds(100) },
            SubstituteLogger<WorkQueueHostedService>());

        await service.StartAsync(TestContext.Current.CancellationToken);
        await processor.StartedCount.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, processor.MaximumConcurrency);
    }

    [Fact]
    public async Task AcknowledgementFailureIsObservedWithoutReportingSuccess()
    {
        var queue = new FakeWorkQueue { AcknowledgeFailure = new InvalidOperationException("ack failed") };
        var processor = new RecordingProcessor();
        queue.Add(CreateQueuedItem("1-0"));

        await using var service = new WorkQueueHostedService(
            queue,
            processor,
            new WorkQueueOptions { MessageWorkerCount = 1 },
            SubstituteLogger<WorkQueueHostedService>());

        await service.StartAsync(TestContext.Current.CancellationToken);
        await processor.Processed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Single(queue.AcknowledgedAttempts);
    }

    [Fact]
    public async Task ShutdownDrainsBufferedInteractionsBeforeReturning()
    {
        var queue = new FakeWorkQueue();
        var channel = new InteractionWorkChannel(new WorkQueueOptions { InteractionChannelCapacity = 2 });
        await channel.WriteAsync(null!, CancellationToken.None);
        await channel.WriteAsync(null!, CancellationToken.None);
        var processed = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var service = new WorkQueueHostedService(
            queue,
            new RecordingProcessor(),
            new WorkQueueOptions { InteractionWorkerCount = 1, ShutdownDrainTimeout = TimeSpan.FromSeconds(1) },
            SubstituteLogger<WorkQueueHostedService>(),
            interactionChannel: channel,
            interactionProcessor: (_, _) =>
            {
                var count = Interlocked.Increment(ref processed);
                started.TrySetResult();
                if (count == 2)
                {
                    drained.TrySetResult();
                }

                return Task.CompletedTask;
            });

        await service.StartAsync(TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        service.StopIntake();
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.True(drained.Task.IsCompletedSuccessfully);
        Assert.Equal(2, processed);
    }

    [Fact]
    public async Task ShutdownReturnsAfterTimeoutWhenProcessorIgnoresCancellation()
    {
        var queue = new FakeWorkQueue();
        var processor = new RecordingProcessor { NonCooperative = true };
        queue.Add(CreateQueuedItem("1-0"));

        await using var service = new WorkQueueHostedService(
            queue,
            processor,
            new WorkQueueOptions { ShutdownDrainTimeout = TimeSpan.FromMilliseconds(50) },
            SubstituteLogger<WorkQueueHostedService>());

        await service.StartAsync(TestContext.Current.CancellationToken);
        await processor.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var stop = service.StopAsync(TestContext.Current.CancellationToken);
        await stop.WaitAsync(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WorkerMessageAdmissionUsesShutdownCancellation()
    {
        var queue = new FakeWorkQueue();
        await using var queueService = CreateQueueService(queue);
        var worker = CreateWorker(queue, queueService);
        queueService.StopIntake();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            worker.AdmitMessageAsync(CreateQueuedItem("1-0").Item));
    }

    [Fact]
    public async Task WorkerInteractionAdmissionUsesShutdownCancellationAfterDefer()
    {
        var queue = new FakeWorkQueue();
        await using var queueService = CreateQueueService(queue);
        var worker = CreateWorker(queue, queueService);
        var deferred = false;
        queueService.StopIntake();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            worker.AdmitInteractionAsync(new RecordingInteraction(), () =>
            {
                deferred = true;
                return Task.CompletedTask;
            }));

        Assert.True(deferred);
    }

    [Fact]
    public async Task FailedInteractionReceivesARealTerminalFollowup()
    {
        var queue = new FakeWorkQueue();
        var channel = new InteractionWorkChannel(new WorkQueueOptions { InteractionChannelCapacity = 1 });
        var interaction = new RecordingInteraction();
        await channel.WriteAsync(interaction, CancellationToken.None);

        await using var service = new WorkQueueHostedService(
            queue,
            new RecordingProcessor(),
            new WorkQueueOptions { ShutdownDrainTimeout = TimeSpan.FromMilliseconds(100) },
            SubstituteLogger<WorkQueueHostedService>(),
            interactionChannel: channel,
            interactionProcessor: (_, _) => Task.FromException(new InvalidOperationException("failed")));

        await service.StartAsync(TestContext.Current.CancellationToken);
        await interaction.FollowupSent.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Failed to process this interaction.", interaction.FollowupContent);
    }

    private static QueuedMessageWorkItem CreateQueuedItem(string entryId) => new(entryId,
        new MessageWorkItem(1, 2, 3, 4, [], "content", null, [], true, true, Guid.NewGuid()));

    private static ILogger<T> SubstituteLogger<T>() =>
        Microsoft.Extensions.Logging.Abstractions.NullLogger<T>.Instance;

    private sealed class RecordingProcessor : IWorkItemProcessor
    {
        public List<QueuedMessageWorkItem> Items { get; } = [];
        public TaskCompletionSource Processed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StartedCount { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool ThrowOnFirstItem { get; init; }
        public bool Block { get; init; }
        public bool NonCooperative { get; init; }
        public bool CancellationObserved { get; private set; }
        public TaskCompletionSource CancellationObservedSource { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int MaximumConcurrency { get; private set; }
        private int _concurrency;

        public async Task ProcessAsync(QueuedMessageWorkItem item, CancellationToken cancellationToken)
        {
            Items.Add(item);
            Started.TrySetResult();
            var concurrency = Interlocked.Increment(ref _concurrency);
            MaximumConcurrency = Math.Max(MaximumConcurrency, concurrency);
            if (Items.Count >= 2)
            {
                StartedCount.TrySetResult();
            }

            if (Block)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    CancellationObserved = true;
                    CancellationObservedSource.TrySetResult();
                    throw;
                }
            }

            if (NonCooperative)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan);
            }

            if (ThrowOnFirstItem && Items.Count == 1)
            {
                throw new InvalidOperationException("processor failure");
            }

            Processed.TrySetResult();
            Interlocked.Decrement(ref _concurrency);
        }
    }

    private static WorkQueueHostedService CreateQueueService(FakeWorkQueue queue) => new(
        queue,
        new RecordingProcessor(),
        new WorkQueueOptions { ShutdownDrainTimeout = TimeSpan.FromMilliseconds(100) },
        SubstituteLogger<WorkQueueHostedService>());

    private static Worker CreateWorker(FakeWorkQueue queue, WorkQueueHostedService queueService) => new(
        Microsoft.Extensions.Logging.Abstractions.NullLogger<Worker>.Instance,
        new ConfigurationBuilder().Build(),
        new NullDatabaseMigrator(),
        new InteractionHandler(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<InteractionHandler>.Instance,
            new ServiceCollection().BuildServiceProvider()),
        queue,
        new InteractionWorkChannel(new WorkQueueOptions()),
        queueService,
        new SaucyBotMetrics());

    private sealed class RecordingInteraction : IInteractionWorkItem
    {
        public ulong Id => 42;
        public Discord.WebSocket.SocketInteraction? SocketInteraction => null;
        public TaskCompletionSource FollowupSent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? FollowupContent { get; private set; }

        public Task FollowupAsync(string content, bool ephemeral, CancellationToken cancellationToken = default)
        {
            FollowupContent = content;
            FollowupSent.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeWorkQueue : IMessageWorkQueue
    {
        private readonly Channel<QueuedMessageWorkItem> _items = Channel.CreateUnbounded<QueuedMessageWorkItem>();

        public List<QueuedMessageWorkItem> Acknowledged { get; } = [];
        public List<QueuedMessageWorkItem> AcknowledgedAttempts { get; } = [];
        public Exception? AcknowledgeFailure { get; init; }
        public bool ReadCancellationObserved { get; private set; }

        public void Add(QueuedMessageWorkItem item) => _items.Writer.TryWrite(item);

        public Task EnqueueAsync(MessageWorkItem item, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new NotSupportedException();
        }

        public async IAsyncEnumerable<QueuedMessageWorkItem> ReadAsync(
            string consumer,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            using var registration = cancellationToken.Register(() => ReadCancellationObserved = true);
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
                    throw;
                }

                yield return item;
            }
        }

        public Task AcknowledgeAsync(QueuedMessageWorkItem item, CancellationToken cancellationToken)
        {
            AcknowledgedAttempts.Add(item);
            if (AcknowledgeFailure is not null)
            {
                return Task.FromException(AcknowledgeFailure);
            }

            Acknowledged.Add(item);
            return Task.CompletedTask;
        }

        public Task ClearPendingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
