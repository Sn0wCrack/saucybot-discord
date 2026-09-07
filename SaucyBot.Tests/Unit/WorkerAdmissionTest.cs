using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Discord;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SaucyBot;
using SaucyBot.Diagnostics;
using SaucyBot.Library.Discord;
using SaucyBot.Queue;
using SaucyBot.Services;
using Xunit;

namespace SaucyBot.Tests.Unit;

public sealed class WorkerAdmissionTest
{
    [Theory]
    [InlineData("sauce", true)]
    [InlineData("settings", false)]
    [InlineData("unknown", false)]
    public void InteractionAcknowledgementPolicyOnlyDefersLongRunningCommands(string commandName, bool expected)
    {
        Assert.Equal(expected, InteractionAcknowledgementPolicy.ShouldDefer(commandName));
    }

    [Fact]
    public void SettingsInteractionUsesImmediateExecutionPolicy()
    {
        Assert.True(InteractionAcknowledgementPolicy.ShouldExecuteImmediately("settings"));
        Assert.False(InteractionAcknowledgementPolicy.ShouldExecuteImmediately("sauce"));
    }

    [Fact]
    public void NonSlashInteractionsUseImmediateExecutionPolicy()
    {
        var interaction = new RecordingInteraction
        {
            CommandName = null,
            IsSlashCommand = false
        };

        Assert.True(InteractionAcknowledgementPolicy.ShouldExecuteImmediately(interaction));
    }

    [Fact]
    public async Task NonSlashInteractionUsesImmediateExecutionBeforeChannelAdmission()
    {
        var queue = new FakeWorkQueue();
        var channel = new InteractionWorkChannel(new WorkQueueOptions { InteractionChannelCapacity = 1 });
        await channel.WriteAsync(null!, CancellationToken.None);
        await using var queueService = CreateQueueService(queue);
        var processor = new ImmediateInteractionProcessor();
        var worker = CreateWorker(queue, queueService, channel, processor);
        var interaction = new RecordingInteraction
        {
            CommandName = "unknown",
            IsSlashCommand = false
        };

        await worker.AdmitInteractionAsync(interaction);

        Assert.Equal(1, processor.ProcessedCount);
    }

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
            SubstituteLogger<WorkQueueHostedService>(),
            new InteractionWorkChannel(new WorkQueueOptions()),
            Substitute.For<IInteractionProcessor>(),
            new SaucyBotMetrics());

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
            SubstituteLogger<WorkQueueHostedService>(),
            new InteractionWorkChannel(new WorkQueueOptions()),
            Substitute.For<IInteractionProcessor>(),
            new SaucyBotMetrics());

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
            SubstituteLogger<WorkQueueHostedService>(),
            new InteractionWorkChannel(new WorkQueueOptions()),
            Substitute.For<IInteractionProcessor>(),
            new SaucyBotMetrics());

        await service.StartAsync(TestContext.Current.CancellationToken);
        await processor.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);
        await processor.CancellationObservedSource.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.True(processor.CancellationObserved);
        Assert.True(queue.ReadCancellationObserved);
        Assert.Empty(queue.Acknowledged);
    }

    [Fact]
    public async Task HostStoppingTokenCancelsActiveWorkAndQueueRead()
    {
        var queue = new FakeWorkQueue();
        var processor = new RecordingProcessor { Block = true };
        queue.Add(CreateQueuedItem("1-0"));

        await using var service = new WorkQueueHostedService(
            queue,
            processor,
            new WorkQueueOptions { MessageWorkerCount = 1 },
            SubstituteLogger<WorkQueueHostedService>(),
            new InteractionWorkChannel(new WorkQueueOptions()),
            Substitute.For<IInteractionProcessor>(),
            new SaucyBotMetrics());

        using var stopping = new CancellationTokenSource();
        var executeAsync = typeof(WorkQueueHostedService)
            .GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var execution = (Task)executeAsync.Invoke(service, [stopping.Token])!;

        await processor.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        stopping.Cancel();
        processor.Release();
        await queue.ReadCancellationObservedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await execution.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.False(processor.CancellationObserved);
        Assert.True(queue.ReadCancellationObserved);
        Assert.Single(queue.Acknowledged);
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
            interactionChannel,
            Substitute.For<IInteractionProcessor>(),
            new SaucyBotMetrics());

        service.StopIntake();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            interactionChannel.WriteAsync(null!, service.AdmissionToken).AsTask());
    }

    [Fact]
    public async Task StopIntakeCanBeCalledRepeatedly()
    {
        await using var service = CreateQueueService(new FakeWorkQueue());

        service.StopIntake();
        service.StopIntake();

        Assert.True(service.AdmissionToken.IsCancellationRequested);
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
            SubstituteLogger<WorkQueueHostedService>(),
            new InteractionWorkChannel(new WorkQueueOptions()),
            Substitute.For<IInteractionProcessor>(),
            new SaucyBotMetrics());

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
            SubstituteLogger<WorkQueueHostedService>(),
            new InteractionWorkChannel(new WorkQueueOptions()),
            Substitute.For<IInteractionProcessor>(),
            new SaucyBotMetrics());

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
            interactionProcessor: new RecordingInteractionProcessor(() =>
            {
                var count = Interlocked.Increment(ref processed);
                started.TrySetResult();
                if (count == 2)
                {
                    drained.TrySetResult();
                }
            }),
            metrics: new SaucyBotMetrics());

        await service.StartAsync(TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        service.StopIntake();
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.True(drained.Task.IsCompletedSuccessfully);
        Assert.Equal(2, processed);
    }

    [Fact]
    public async Task StartupClearsPendingWorkBeforeWorkersRead()
    {
        var queue = new FakeWorkQueue();
        var processor = new RecordingProcessor();
        queue.ClearPendingCallback = () => Assert.Empty(processor.Items);

        await using var service = new WorkQueueHostedService(
            queue,
            processor,
            new WorkQueueOptions { ClearPendingOnStartup = true },
            SubstituteLogger<WorkQueueHostedService>(),
            new InteractionWorkChannel(new WorkQueueOptions()),
            Substitute.For<IInteractionProcessor>(),
            new SaucyBotMetrics());

        await service.StartAsync(TestContext.Current.CancellationToken);
        service.StopIntake();
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, queue.ClearCalls);
    }

    [Fact]
    public async Task ShutdownDrainsAdmittedMessageBeforeReturning()
    {
        var queue = new FakeWorkQueue();
        var processor = new RecordingProcessor { Block = true };
        var item = CreateQueuedItem("1-0");
        queue.Add(item);

        await using var service = new WorkQueueHostedService(
            queue,
            processor,
            new WorkQueueOptions { ShutdownDrainTimeout = TimeSpan.FromSeconds(1) },
            SubstituteLogger<WorkQueueHostedService>(),
            new InteractionWorkChannel(new WorkQueueOptions()),
            Substitute.For<IInteractionProcessor>(),
            new SaucyBotMetrics());

        await service.StartAsync(TestContext.Current.CancellationToken);
        await processor.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        processor.Release();
        service.StopIntake();
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal([item], queue.Acknowledged);
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
            SubstituteLogger<WorkQueueHostedService>(),
            new InteractionWorkChannel(new WorkQueueOptions()),
            Substitute.For<IInteractionProcessor>(),
            new SaucyBotMetrics());

        await service.StartAsync(TestContext.Current.CancellationToken);
        await processor.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var stop = service.StopAsync(TestContext.Current.CancellationToken);
        await stop.WaitAsync(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StopAsyncHonorsCallerCancellationWhileDraining()
    {
        var queue = new FakeWorkQueue();
        var processor = new RecordingProcessor { NonCooperative = true };
        queue.Add(CreateQueuedItem("1-0"));

        await using var service = new WorkQueueHostedService(
            queue,
            processor,
            new WorkQueueOptions { ShutdownDrainTimeout = TimeSpan.FromSeconds(5) },
            SubstituteLogger<WorkQueueHostedService>(),
            new InteractionWorkChannel(new WorkQueueOptions()),
            Substitute.For<IInteractionProcessor>(),
            new SaucyBotMetrics());

        await service.StartAsync(TestContext.Current.CancellationToken);
        await processor.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.StopAsync(cancellation.Token));

        Assert.Empty(queue.Acknowledged);
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
    public async Task WorkerInteractionAdmissionUsesShutdownCancellationWithoutPredeferring()
    {
        var queue = new FakeWorkQueue();
        await using var queueService = CreateQueueService(queue);
        var worker = CreateWorker(queue, queueService);
        queueService.StopIntake();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => worker.AdmitInteractionAsync(
            new RecordingInteraction()));
    }

    [Fact]
    public async Task SauceInteractionDefersBeforeWaitingForChannelAdmission()
    {
        var queue = new FakeWorkQueue();
        var channel = new InteractionWorkChannel(new WorkQueueOptions { InteractionChannelCapacity = 1 });
        await channel.WriteAsync(null!, CancellationToken.None);
        await using var queueService = CreateQueueService(queue);
        var worker = CreateWorker(queue, queueService, channel);
        var interaction = new RecordingInteraction { CommandName = "sauce" };

        var admission = worker.AdmitInteractionAsync(interaction);
        await interaction.Deferred.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        queueService.StopIntake();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => admission);
    }

    [Fact]
    public async Task SettingsInteractionIsAdmittedWithoutPredeferring()
    {
        var queue = new FakeWorkQueue();
        var channel = new InteractionWorkChannel(new WorkQueueOptions { InteractionChannelCapacity = 1 });
        await using var queueService = CreateQueueService(queue);
        var worker = CreateWorker(queue, queueService, channel);
        var interaction = new RecordingInteraction { CommandName = "settings" };

        await worker.AdmitInteractionAsync(interaction);

        Assert.Null(interaction.ResponseKind);
    }

    [Fact]
    public async Task SettingsInteractionBypassesFullChannelForInitialResponse()
    {
        var queue = new FakeWorkQueue();
        var channel = new InteractionWorkChannel(new WorkQueueOptions { InteractionChannelCapacity = 1 });
        await channel.WriteAsync(null!, CancellationToken.None);
        await using var queueService = CreateQueueService(queue);
        var interaction = new RecordingInteraction { CommandName = "settings" };
        var processor = new ImmediateInteractionProcessor();
        var worker = CreateWorker(queue, queueService, channel, processor);

        await worker.AdmitInteractionAsync(interaction);

        Assert.Equal(1, processor.ProcessedCount);
        Assert.Equal("initial", interaction.ResponseKind);
        Assert.Equal("settings modal", interaction.ResponseContent);
    }

    [Fact]
    public void MessageWorkItemCreateReturnsNullForNonUserMessages()
    {
        var message = (Discord.WebSocket.SocketMessage)RuntimeHelpers.GetUninitializedObject(
            typeof(Discord.WebSocket.SocketSystemMessage));

        Assert.Null(MessageWorkItem.Create(message));
    }

    private static QueuedMessageWorkItem CreateQueuedItem(string entryId) => new(entryId,
        new MessageWorkItem(1, 2, 3, 4, [], "content", null, [], true, true,
            Guid.Parse("11111111-1111-1111-1111-111111111111")));

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
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
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
                    await _release.Task.WaitAsync(cancellationToken);
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

        public void Release() => _release.TrySetResult();
    }

    private sealed class RecordingInteractionProcessor(Action callback) : IInteractionProcessor
    {
        public Task ProcessAsync(IInteractionWorkItem interaction, CancellationToken cancellationToken)
        {
            callback();
            return Task.CompletedTask;
        }
    }

    private sealed class ImmediateInteractionProcessor : IInteractionProcessor
    {
        public int ProcessedCount { get; private set; }

        public Task ProcessAsync(IInteractionWorkItem interaction, CancellationToken cancellationToken)
        {
            ProcessedCount++;
            return interaction.RespondAsync("settings modal", ephemeral: false, cancellationToken);
        }
    }

    private static WorkQueueHostedService CreateQueueService(FakeWorkQueue queue) => new(
        queue,
        new RecordingProcessor(),
        new WorkQueueOptions { ShutdownDrainTimeout = TimeSpan.FromMilliseconds(100) },
        SubstituteLogger<WorkQueueHostedService>(),
        new InteractionWorkChannel(new WorkQueueOptions()),
        Substitute.For<IInteractionProcessor>(),
        new SaucyBotMetrics());

    private static Worker CreateWorker(
        FakeWorkQueue queue,
        WorkQueueHostedService queueService,
        InteractionWorkChannel? interactionChannel = null,
        IInteractionProcessor? interactionProcessor = null)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        return new Worker(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Worker>.Instance,
            new ConfigurationBuilder().Build(),
            Substitute.For<IDatabaseMigrator>(),
            new InteractionHandler(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<InteractionHandler>.Instance,
                services),
            queue,
            new SiteRegistry(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SiteRegistry>.Instance,
                new ConfigurationBuilder().Build(),
                services,
                []),
            interactionChannel ?? new InteractionWorkChannel(new WorkQueueOptions()),
            interactionProcessor ?? Substitute.For<IInteractionProcessor>(),
            queueService,
            new SaucyBotMetrics(),
            Substitute.For<IMessageResolver>());
    }

    private sealed class RecordingInteraction : IInteractionWorkItem
    {
        public ulong Id => 42;
        public Discord.WebSocket.SocketInteraction? SocketInteraction { get; init; }
        public bool IsSlashCommand { get; init; } = true;
        public string? CommandName { get; init; }
        public TaskCompletionSource FollowupSent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Deferred { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool HasResponded { get; set; }
        public string? ResponseContent { get; private set; }
        public string? ResponseKind { get; private set; }
        public CancellationToken LastResponseCancellationToken { get; private set; }

        public Task DeferAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HasResponded = true;
            ResponseKind = "defer";
            Deferred.TrySetResult();
            return Task.CompletedTask;
        }

        public Task RespondAsync(
            string content,
            bool ephemeral,
            CancellationToken cancellationToken = default,
            TimeSpan? timeout = null)
        {
            HasResponded = true;
            ResponseContent = content;
            ResponseKind = "initial";
            LastResponseCancellationToken = cancellationToken;
            FollowupSent.TrySetResult();
            return Task.CompletedTask;
        }

        public Task FollowupAsync(
            string content,
            bool ephemeral,
            CancellationToken cancellationToken = default,
            TimeSpan? timeout = null)
        {
            ResponseContent = content;
            ResponseKind = "followup";
            LastResponseCancellationToken = cancellationToken;
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
        public TaskCompletionSource ReadCancellationObservedSignal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ClearCalls { get; private set; }
        public Action? ClearPendingCallback { get; set; }

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
            using var registration = cancellationToken.Register(() =>
            {
                ReadCancellationObserved = true;
                ReadCancellationObservedSignal.TrySetResult();
            });
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

        public Task DeleteAsync(string entryId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ClearPendingAsync(CancellationToken cancellationToken)
        {
            ClearCalls++;
            ClearPendingCallback?.Invoke();
            return Task.CompletedTask;
        }
    }
}
