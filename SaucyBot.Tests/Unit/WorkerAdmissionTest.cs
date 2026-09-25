using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
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
using SaucyBot.Queue.Redis;
using SaucyBot.Services;
using SaucyBot.Tests.Unit.Common;
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
        var clientHost = CreateClientHost(queue, queueService, channel, processor);
        var interaction = new RecordingInteraction
        {
            CommandName = "unknown",
            IsSlashCommand = false
        };

        await clientHost.AdmitInteractionAsync(interaction);

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
            new WorkQueueOptions { MessageWorkerCount = 1 },
            SubstituteLogger<WorkQueueHostedService>(),
            new InteractionWorkChannel(new WorkQueueOptions()),
            Substitute.For<IInteractionProcessor>(),
            new SaucyBotMetrics(),
            new DelegatingExecutor(queue, processor));

        await service.StartAsync(TestContext.Current.CancellationToken);
        await processor.Processed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal([item.Item], processor.Items);
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
            new WorkQueueOptions { MessageWorkerCount = 1 },
            SubstituteLogger<WorkQueueHostedService>(),
            new InteractionWorkChannel(new WorkQueueOptions()),
            Substitute.For<IInteractionProcessor>(),
            new SaucyBotMetrics(),
            new DelegatingExecutor(queue, processor));

        await service.StartAsync(TestContext.Current.CancellationToken);
        await processor.Processed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal([failed.Item, succeeded.Item], processor.Items);
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
            new WorkQueueOptions
            {
                MessageWorkerCount = 1,
                ShutdownDrainTimeout = TimeSpan.FromMilliseconds(100)
            },
            SubstituteLogger<WorkQueueHostedService>(),
            new InteractionWorkChannel(new WorkQueueOptions()),
            Substitute.For<IInteractionProcessor>(),
            new SaucyBotMetrics(),
            new DelegatingExecutor(queue, processor));

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
            new WorkQueueOptions { MessageWorkerCount = 1 },
            SubstituteLogger<WorkQueueHostedService>(),
            new InteractionWorkChannel(new WorkQueueOptions()),
            Substitute.For<IInteractionProcessor>(),
            new SaucyBotMetrics(),
            new DelegatingExecutor(queue, processor));

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
            new WorkQueueOptions { MessageWorkerCount = 1, ShutdownDrainTimeout = TimeSpan.FromMilliseconds(100) },
            SubstituteLogger<WorkQueueHostedService>(),
            interactionChannel,
            Substitute.For<IInteractionProcessor>(),
            new SaucyBotMetrics(),
            new DelegatingExecutor(queue, processor));

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
            new WorkQueueOptions { MessageWorkerCount = 2, ShutdownDrainTimeout = TimeSpan.FromMilliseconds(100) },
            SubstituteLogger<WorkQueueHostedService>(),
            new InteractionWorkChannel(new WorkQueueOptions()),
            Substitute.For<IInteractionProcessor>(),
            new SaucyBotMetrics(),
            new DelegatingExecutor(queue, processor));

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
            new WorkQueueOptions { MessageWorkerCount = 1 },
            SubstituteLogger<WorkQueueHostedService>(),
            new InteractionWorkChannel(new WorkQueueOptions()),
            Substitute.For<IInteractionProcessor>(),
            new SaucyBotMetrics(),
            new DelegatingExecutor(queue, processor));

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
        var messageProcessor = new RecordingProcessor();

        await using var service = new WorkQueueHostedService(
            queue,
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
            metrics: new SaucyBotMetrics(),
            executor: new DelegatingExecutor(queue, messageProcessor));

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
            new WorkQueueOptions(),
            SubstituteLogger<WorkQueueHostedService>(),
            new InteractionWorkChannel(new WorkQueueOptions()),
            Substitute.For<IInteractionProcessor>(),
            new SaucyBotMetrics(),
            new DelegatingExecutor(queue, processor));

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
            new WorkQueueOptions { ShutdownDrainTimeout = TimeSpan.FromSeconds(1) },
            SubstituteLogger<WorkQueueHostedService>(),
            new InteractionWorkChannel(new WorkQueueOptions()),
            Substitute.For<IInteractionProcessor>(),
            new SaucyBotMetrics(),
            new DelegatingExecutor(queue, processor));

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
            new WorkQueueOptions { ShutdownDrainTimeout = TimeSpan.FromMilliseconds(50) },
            SubstituteLogger<WorkQueueHostedService>(),
            new InteractionWorkChannel(new WorkQueueOptions()),
            Substitute.For<IInteractionProcessor>(),
            new SaucyBotMetrics(),
            new DelegatingExecutor(queue, processor));

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
            new WorkQueueOptions { ShutdownDrainTimeout = TimeSpan.FromSeconds(5) },
            SubstituteLogger<WorkQueueHostedService>(),
            new InteractionWorkChannel(new WorkQueueOptions()),
            Substitute.For<IInteractionProcessor>(),
            new SaucyBotMetrics(),
            new DelegatingExecutor(queue, processor));

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
        var clientHost = CreateClientHost(queue, queueService);
        queueService.StopIntake();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            clientHost.AdmitMessageAsync(CreateQueuedItem("1-0").Item));
    }

    [Fact]
    public async Task MessageAdmissionPassesTheConfiguredEnqueueTimeoutToTheProducer()
    {
        var queue = new FakeWorkQueue();
        await using var queueService = CreateQueueService(queue);
        var clientHost = CreateClientHost(
            queue,
            queueService,
            workQueueOptions: new WorkQueueOptions { EnqueueTimeout = TimeSpan.FromSeconds(7) });

        await clientHost.AdmitMessageAsync(CreateQueuedItem("1-0").Item);

        Assert.Equal(TimeSpan.FromSeconds(7), queue.LastEnqueueTimeout);
    }

    [Fact]
    public async Task MessageAdmissionRecordsEnqueueTimedOutWhenRedisAddHangsWithoutLoggingContent()
    {
        var client = new HangingAddRedisClient();
        using var metrics = new SaucyBotMetrics();
        long enqueueTimedOut = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (ReferenceEquals(instrument, metrics.EnqueueTimedOut))
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, _, _) =>
        {
            enqueueTimedOut += measurement;
        });
        listener.Start();
        var producer = new RedisWorkQueue(
            client,
            new WorkQueueOptions
            {
                EnqueueTimeout = TimeSpan.FromMilliseconds(100),
                BackendOperationTimeout = TimeSpan.FromMilliseconds(100),
            },
            new RedisWorkQueueOptions { RetryDelay = TimeSpan.Zero },
            metrics);
        var logger = new RecordingLogger<DiscordClientHost>();
        await using var queueService = CreateQueueService(new FakeWorkQueue());
        var clientHost = CreateClientHost(
            producer,
            queueService,
            workQueueOptions: new WorkQueueOptions { EnqueueTimeout = TimeSpan.FromMilliseconds(100) },
            metrics: metrics,
            logger: logger);
        var item = TestData.Message() with { Content = "super-secret-content" };

        try
        {
            await clientHost.AdmitMessageAsync(item).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        finally
        {
            client.AddCompletion.TrySetResult("1-0");
        }

        Assert.Equal(1, enqueueTimedOut);
        Assert.Contains(logger.Messages, message => message.Contains("timed out", StringComparison.Ordinal));
        Assert.DoesNotContain("super-secret-content", string.Join("\n", logger.Messages));
    }

    [Fact]
    public async Task WorkerInteractionAdmissionUsesShutdownCancellationWithoutPredeferring()
    {
        var queue = new FakeWorkQueue();
        await using var queueService = CreateQueueService(queue);
        var clientHost = CreateClientHost(queue, queueService);
        queueService.StopIntake();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => clientHost.AdmitInteractionAsync(
            new RecordingInteraction()));
    }

    [Fact]
    public async Task SauceInteractionDefersBeforeWaitingForChannelAdmission()
    {
        var queue = new FakeWorkQueue();
        var channel = new InteractionWorkChannel(new WorkQueueOptions { InteractionChannelCapacity = 1 });
        await channel.WriteAsync(null!, CancellationToken.None);
        await using var queueService = CreateQueueService(queue);
        var clientHost = CreateClientHost(queue, queueService, channel);
        var interaction = new RecordingInteraction { CommandName = "sauce" };

        var admission = clientHost.AdmitInteractionAsync(interaction);
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
        var clientHost = CreateClientHost(queue, queueService, channel);
        var interaction = new RecordingInteraction { CommandName = "settings" };

        await clientHost.AdmitInteractionAsync(interaction);

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
        var clientHost = CreateClientHost(queue, queueService, channel, processor);

        await clientHost.AdmitInteractionAsync(interaction);

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
            Guid.Parse("11111111-1111-1111-1111-111111111111")),
        new TestData.NoOpWorkItemLease());

    private static ILogger<T> SubstituteLogger<T>() =>
        Microsoft.Extensions.Logging.Abstractions.NullLogger<T>.Instance;

    private sealed class RecordingProcessor : IWorkItemProcessor
    {
        public List<MessageWorkItem> Items { get; } = [];
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

        public async Task ProcessAsync(WorkDelivery<MessageWorkItem> delivery, CancellationToken cancellationToken)
        {
            Items.Add(delivery.Item);
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

    private sealed class DelegatingExecutor(
        FakeWorkQueue queue,
        IWorkItemProcessor processor) : IQueuedWorkItemExecutor
    {
        public async Task<QueuedWorkItemExecutionOutcome> ExecuteAsync(
            string consumer,
            QueuedMessageWorkItem item,
            CancellationToken cancellationToken)
        {
            try
            {
                await processor.ProcessAsync(item.ToDelivery(), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                await queue.CompleteAsync(item, CancellationToken.None);
                return QueuedWorkItemExecutionOutcome.Completed;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return QueuedWorkItemExecutionOutcome.Cancelled;
            }
            catch (Exception exception)
            {
                await queue.FailAsync(item, exception, CancellationToken.None);
                return QueuedWorkItemExecutionOutcome.Failed;
            }
        }
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
        new WorkQueueOptions { ShutdownDrainTimeout = TimeSpan.FromMilliseconds(100) },
        SubstituteLogger<WorkQueueHostedService>(),
        new InteractionWorkChannel(new WorkQueueOptions()),
        Substitute.For<IInteractionProcessor>(),
        new SaucyBotMetrics(),
        new DelegatingExecutor(queue, new RecordingProcessor()));

    private static DiscordClientHost CreateClientHost(
        IWorkItemProducer<MessageWorkItem> producer,
        WorkQueueHostedService queueService,
        InteractionWorkChannel? interactionChannel = null,
        IInteractionProcessor? interactionProcessor = null,
        WorkQueueOptions? workQueueOptions = null,
        ISaucyBotMetrics? metrics = null,
        ILogger<DiscordClientHost>? logger = null)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        return new DiscordClientHost(
            logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DiscordClientHost>.Instance,
            new ConfigurationBuilder().Build().BotOptions(),
            producer,
            workQueueOptions ?? new WorkQueueOptions(),
            new SiteRegistry(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SiteRegistry>.Instance,
                new ConfigurationBuilder().Build().BotOptions(),
                services,
                []),
            interactionChannel ?? new InteractionWorkChannel(new WorkQueueOptions()),
            interactionProcessor ?? Substitute.For<IInteractionProcessor>(),
            queueService,
            metrics ?? new SaucyBotMetrics(),
            new InteractionHandler(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<InteractionHandler>.Instance,
                services),
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

    private sealed class FakeWorkQueue : IMessageWorkQueue, IWorkItemProducer<MessageWorkItem>
    {
        private readonly Channel<QueuedMessageWorkItem> _items = Channel.CreateUnbounded<QueuedMessageWorkItem>();

        public List<QueuedMessageWorkItem> Acknowledged { get; } = [];
        public List<QueuedMessageWorkItem> AcknowledgedAttempts { get; } = [];
        public Exception? AcknowledgeFailure { get; init; }
        public bool ReadCancellationObserved { get; private set; }
        public TaskCompletionSource ReadCancellationObservedSignal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ClearCalls { get; private set; }
        public Action? ClearPendingCallback { get; set; }
        public TimeSpan? LastEnqueueTimeout { get; private set; }

        public void Add(QueuedMessageWorkItem item) => _items.Writer.TryWrite(item);

        public Task EnqueueAsync(MessageWorkItem item, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new NotSupportedException();
        }

        public Task<EnqueueResult> EnqueueAsync(
            MessageWorkItem item,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastEnqueueTimeout = timeout;
            return Task.FromResult(EnqueueResult.Accepted);
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

        public async IAsyncEnumerable<QueuedMessageWorkItem> ReclaimAsync(
            string consumer,
            TimeSpan minimumIdleTime,
            int count,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            ClearCalls++;
            ClearPendingCallback?.Invoke();
            return Task.CompletedTask;
        }

        public Task CompleteAsync(QueuedMessageWorkItem item, CancellationToken cancellationToken)
        {
            AcknowledgedAttempts.Add(item);
            if (AcknowledgeFailure is not null)
            {
                return Task.FromException(AcknowledgeFailure);
            }

            Acknowledged.Add(item);
            return Task.CompletedTask;
        }

        public Task<WorkItemFailureResult> FailAsync(
            QueuedMessageWorkItem item,
            Exception exception,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new WorkItemFailureResult(WorkItemFailureAction.Retried, item.DeliveryCount));
        }
    }

    private sealed class HangingAddRedisClient : IRedisStreamClient
    {
        public TaskCompletionSource<string> AddCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task EnsureGroupAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<string> AddAsync(string payload, CancellationToken cancellationToken) => AddCompletion.Task;

        public Task<RedisStreamEntry?> ReadNewAsync(
            string consumer,
            string leaseToken,
            CancellationToken cancellationToken) =>
            Task.FromResult<RedisStreamEntry?>(null);

        public Task<bool> RenewAsync(
            string consumer,
            string entryId,
            string leaseToken,
            CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<RedisStreamEntry?> ReclaimAsync(
            string consumer,
            TimeSpan minimumIdleTime,
            string leaseToken,
            CancellationToken cancellationToken) =>
            Task.FromResult<RedisStreamEntry?>(null);

        public Task<LeaseOperationResult> CompleteAsync(
            string consumer,
            string entryId,
            string leaseToken,
            CancellationToken cancellationToken) =>
            Task.FromResult(LeaseOperationResult.Applied);

        public Task<LeaseOperationResult> RetryAsync(
            string consumer,
            string entryId,
            string leaseToken,
            CancellationToken cancellationToken) =>
            Task.FromResult(LeaseOperationResult.Applied);

        public Task ClearPendingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
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
}
