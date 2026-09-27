using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Discord;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
        await channel.WriteAsync(new RecordingInteraction(), CancellationToken.None);
        await using var queueService = CreateQueueService(queue);
        var processor = Substitute.For<IInteractionProcessor>();
        processor.ProcessAsync(Arg.Any<IInteractionWorkItem>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var clientHost = CreateClientHost(queue, queueService, channel, processor);
        var interaction = new RecordingInteraction
        {
            CommandName = "unknown",
            IsSlashCommand = false
        };

        await clientHost.AdmitInteractionAsync(interaction);

        await processor.Received(1).ProcessAsync(interaction, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WorkersProcessItemsAndAcknowledgeOnlyAfterSuccessfulProcessing()
    {
        var queue = new FakeWorkQueue();
        var processedItems = new List<MessageWorkItem>();
        var processed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processor = Substitute.For<IWorkItemProcessor>();
        processor.ProcessAsync(Arg.Any<MessageWorkItem>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                processedItems.Add(call.Arg<MessageWorkItem>());
                processed.TrySetResult();
                return Task.CompletedTask;
            });
        var item = CreateDelivery("1-0");
        queue.Add(item);

        await using var service = CreateQueueService(queue, new WorkQueueOptions { MessageWorkerCount = 1 }, processor);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await processed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal([item.Item], processedItems);
        Assert.Equal([item.DeliveryId], queue.Acknowledged);
    }

    [Fact]
    public async Task WorkerExceptionsAreObservedAndDoNotStopOtherWorkers()
    {
        var queue = new FakeWorkQueue();
        var processedItems = new List<MessageWorkItem>();
        var processed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var processor = Substitute.For<IWorkItemProcessor>();
        processor.ProcessAsync(Arg.Any<MessageWorkItem>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                processedItems.Add(call.Arg<MessageWorkItem>());
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    return Task.FromException(new InvalidOperationException("processor failure"));
                }

                processed.TrySetResult();
                return Task.CompletedTask;
            });
        var failed = CreateDelivery("1-0");
        var succeeded = CreateDelivery("2-0");
        queue.Add(failed);
        queue.Add(succeeded);

        await using var service = CreateQueueService(queue, new WorkQueueOptions { MessageWorkerCount = 1 }, processor);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await processed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal([failed.Item, succeeded.Item], processedItems);
        Assert.Equal([succeeded.DeliveryId], queue.Acknowledged);
    }

    [Fact]
    public async Task ShutdownCancelsActiveWorkAndStopsReading()
    {
        var queue = new FakeWorkQueue();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processor = Substitute.For<IWorkItemProcessor>();
        processor.ProcessAsync(Arg.Any<MessageWorkItem>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
        {
            started.TrySetResult();
            try
            {
                await release.Task.WaitAsync(call.Arg<CancellationToken>());
            }
            catch (OperationCanceledException)
            {
                cancellationObserved.TrySetResult();
                throw;
            }
        });
        var item = CreateDelivery("1-0");
        queue.Add(item);

        await using var service = CreateQueueService(queue, new WorkQueueOptions
        {
            MessageWorkerCount = 1,
            ShutdownDrainTimeout = TimeSpan.FromMilliseconds(100)
        }, processor);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.True(queue.ReadCancellationObserved);
        Assert.Empty(queue.Acknowledged);
    }

    [Fact]
    public async Task ShutdownStopsAdmissionBeforeDrainingWorkers()
    {
        var queue = new FakeWorkQueue();
        var processor = Substitute.For<IWorkItemProcessor>();
        var interactionChannel = new InteractionWorkChannel(new WorkQueueOptions { InteractionChannelCapacity = 1 });
        await interactionChannel.WriteAsync(null!, CancellationToken.None);

        await using var service = CreateQueueService(
            queue,
            new WorkQueueOptions { MessageWorkerCount = 1, ShutdownDrainTimeout = TimeSpan.FromMilliseconds(100) },
            processor,
            interactionChannel);

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
        var active = 0;
        var maximumConcurrency = 0;
        var startedCount = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processor = Substitute.For<IWorkItemProcessor>();
        processor.ProcessAsync(Arg.Any<MessageWorkItem>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
        {
            var concurrency = Interlocked.Increment(ref active);
            if (concurrency > maximumConcurrency)
            {
                maximumConcurrency = concurrency;
            }

            if (concurrency >= 2)
            {
                startedCount.TrySetResult();
            }

            try
            {
                await release.Task.WaitAsync(call.Arg<CancellationToken>());
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        });
        queue.Add(CreateDelivery("1-0"));
        queue.Add(CreateDelivery("2-0"));

        await using var service = CreateQueueService(
            queue,
            new WorkQueueOptions { MessageWorkerCount = 2, ShutdownDrainTimeout = TimeSpan.FromMilliseconds(100) },
            processor);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await startedCount.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, maximumConcurrency);
    }

    [Fact]
    public async Task AcknowledgementFailureIsObservedWithoutReportingSuccess()
    {
        var queue = new FakeWorkQueue { AcknowledgeFailure = new InvalidOperationException("ack failed") };
        var processed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processor = Substitute.For<IWorkItemProcessor>();
        processor.ProcessAsync(Arg.Any<MessageWorkItem>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                processed.TrySetResult();
                return Task.CompletedTask;
            });
        queue.Add(CreateDelivery("1-0"));

        await using var service = CreateQueueService(queue, new WorkQueueOptions { MessageWorkerCount = 1 }, processor);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await processed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Empty(queue.Acknowledged);
    }

    [Fact]
    public async Task ShutdownDrainsBufferedInteractionsBeforeReturning()
    {
        var queue = new FakeWorkQueue();
        var channel = new InteractionWorkChannel(new WorkQueueOptions { InteractionChannelCapacity = 2 });
        await channel.WriteAsync(new RecordingInteraction(), CancellationToken.None);
        await channel.WriteAsync(new RecordingInteraction(), CancellationToken.None);
        var processed = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var interactionProcessor = Substitute.For<IInteractionProcessor>();
        interactionProcessor.ProcessAsync(Arg.Any<IInteractionWorkItem>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var count = Interlocked.Increment(ref processed);
                started.TrySetResult();
                if (count == 2)
                {
                    drained.TrySetResult();
                }

                return Task.CompletedTask;
            });
        await using var service = CreateQueueService(
            queue,
            new WorkQueueOptions { InteractionWorkerCount = 1, ShutdownDrainTimeout = TimeSpan.FromSeconds(1) },
            interactionChannel: channel,
            interactionProcessor: interactionProcessor);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        service.StopIntake();
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.True(drained.Task.IsCompletedSuccessfully);
        Assert.Equal(2, processed);
    }

    [Fact]
    public async Task QueueBackendStartsBeforeWorkersRead()
    {
        var queue = new FakeWorkQueue();
        var processor = Substitute.For<IWorkItemProcessor>();
        queue.OnStart = () => Assert.Empty(processor.ReceivedCalls());

        await using var service = CreateQueueService(queue, new WorkQueueOptions(), processor);

        await service.StartAsync(TestContext.Current.CancellationToken);
        service.StopIntake();
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, queue.StartCalls);
    }

    [Fact]
    public async Task ShutdownDrainsAdmittedMessageBeforeReturning()
    {
        var queue = new FakeWorkQueue();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processor = Substitute.For<IWorkItemProcessor>();
        processor.ProcessAsync(Arg.Any<MessageWorkItem>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(call.Arg<CancellationToken>());
        });
        var item = CreateDelivery("1-0");
        queue.Add(item);

        await using var service = CreateQueueService(
            queue,
            new WorkQueueOptions { ShutdownDrainTimeout = TimeSpan.FromSeconds(1) },
            processor);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        release.TrySetResult();
        service.StopIntake();
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal([item.DeliveryId], queue.Acknowledged);
    }

    [Fact]
    public async Task ShutdownReturnsAfterTimeoutWhenProcessorIgnoresCancellation()
    {
        var queue = new FakeWorkQueue();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processor = Substitute.For<IWorkItemProcessor>();
        processor.ProcessAsync(Arg.Any<MessageWorkItem>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
        {
            started.TrySetResult();
            await release.Task;
        });
        queue.Add(CreateDelivery("1-0"));

        await using var service = CreateQueueService(
            queue,
            new WorkQueueOptions { ShutdownDrainTimeout = TimeSpan.FromMilliseconds(50) },
            processor);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var stop = service.StopAsync(TestContext.Current.CancellationToken);
        await stop.WaitAsync(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
        release.TrySetResult();
        await service.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StopAsyncHonorsCallerCancellationWhileDraining()
    {
        var queue = new FakeWorkQueue();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processor = Substitute.For<IWorkItemProcessor>();
        processor.ProcessAsync(Arg.Any<MessageWorkItem>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
        {
            started.TrySetResult();
            await release.Task;
        });
        queue.Add(CreateDelivery("1-0"));

        await using var service = CreateQueueService(
            queue,
            new WorkQueueOptions { ShutdownDrainTimeout = TimeSpan.FromSeconds(5) },
            processor);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.StopAsync(cancellation.Token));

        Assert.Empty(queue.Acknowledged);
        release.TrySetResult();
        await service.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WorkerMessageAdmissionUsesShutdownCancellation()
    {
        var queue = new FakeWorkQueue();
        await using var queueService = CreateQueueService(queue);
        var clientHost = CreateClientHost(queue, queueService);
        queueService.StopIntake();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            clientHost.AdmitMessageAsync(CreateDelivery("1-0").Item));
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

        await clientHost.AdmitMessageAsync(CreateDelivery("1-0").Item);

        Assert.Equal(TimeSpan.FromSeconds(7), queue.LastEnqueueTimeout);
    }

    [Fact]
    public async Task AcceptedMessageAdmissionLogsItsMessageIdAtDebugLevel()
    {
        var queue = new FakeWorkQueue();
        var logger = new TestLogger<DiscordClientHost>();
        await using var queueService = CreateQueueService(queue);
        var clientHost = CreateClientHost(queue, queueService, logger: logger);
        var item = TestData.Message();

        await clientHost.AdmitMessageAsync(item);

        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Debug && entry.Message.Contains(item.MessageId.ToString(), StringComparison.Ordinal));
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
            NullLogger<RedisWorkQueue>.Instance,
            metrics);
        var logger = new TestLogger<DiscordClientHost>();
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
        await channel.WriteAsync(new RecordingInteraction(), CancellationToken.None);
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
        await channel.WriteAsync(new RecordingInteraction(), CancellationToken.None);
        await using var queueService = CreateQueueService(queue);
        var interaction = new RecordingInteraction { CommandName = "settings" };
        var processor = Substitute.For<IInteractionProcessor>();
        processor.ProcessAsync(interaction: Arg.Any<IInteractionWorkItem>(), cancellationToken: Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IInteractionWorkItem>().RespondAsync("settings modal", ephemeral: false, call.Arg<CancellationToken>()));
        var clientHost = CreateClientHost(queue, queueService, channel, processor);

        await clientHost.AdmitInteractionAsync(interaction);

        await processor.Received(1).ProcessAsync(interaction, Arg.Any<CancellationToken>());
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

    private static WorkDelivery<MessageWorkItem> CreateDelivery(string entryId, int attempt = 1) => new(
        new MessageWorkItem(1, 2, 3, 4, [], "content", null, [], true, true,
            Guid.Parse("11111111-1111-1111-1111-111111111111")),
        entryId,
        attempt,
        DateTimeOffset.UtcNow,
        TestData.NoOpLease());

    private static ILogger<T> SubstituteLogger<T>() =>
        Microsoft.Extensions.Logging.Abstractions.NullLogger<T>.Instance;

    private static WorkQueueHostedService CreateQueueService(
        FakeWorkQueue queue,
        WorkQueueOptions? options = null,
        IWorkItemProcessor? processor = null,
        InteractionWorkChannel? interactionChannel = null,
        IInteractionProcessor? interactionProcessor = null)
    {
        options ??= new WorkQueueOptions { ShutdownDrainTimeout = TimeSpan.FromMilliseconds(100) };
        processor ??= Substitute.For<IWorkItemProcessor>();
        queue.MaxProcessingAttempts = options.MaxProcessingAttempts;
        var metrics = new SaucyBotMetrics();
        var deliveries = new MessageDeliveryChannel(options);
        interactionChannel ??= new InteractionWorkChannel(options);
        if (interactionProcessor is null)
        {
            interactionProcessor = Substitute.For<IInteractionProcessor>();
            interactionProcessor.ProcessAsync(Arg.Any<IInteractionWorkItem>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);
        }
        var interactionWorker = new InteractionQueueWorker(
            interactionChannel,
            new QueueMiddlewarePipeline<IInteractionWorkItem>(
                [new QueueMetricsMiddleware<IInteractionWorkItem>(metrics)]),
            interactionProcessor,
            SubstituteLogger<InteractionQueueWorker>(),
            metrics);
        return new WorkQueueHostedService(
            queue,
            deliveries,
            new MessageQueueReader(queue, deliveries, SubstituteLogger<MessageQueueReader>()),
            new MessageRecoveryWorker(queue, deliveries, options, SubstituteLogger<MessageRecoveryWorker>(), metrics),
            new MessageQueueWorker(
                deliveries,
                new QueueMiddlewarePipeline<MessageWorkItem>([]),
                processor,
                options,
                SubstituteLogger<MessageQueueWorker>(),
                metrics),
            options,
            SubstituteLogger<WorkQueueHostedService>(),
            interactionChannel,
            interactionWorker,
            metrics);
    }

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
        var options = workQueueOptions ?? new WorkQueueOptions();
        var metricsInstance = metrics ?? new SaucyBotMetrics();
        var interactions = interactionChannel ?? new InteractionWorkChannel(options);
        var interactionWorker = new InteractionQueueWorker(
            interactions,
            new QueueMiddlewarePipeline<IInteractionWorkItem>(
                [new QueueMetricsMiddleware<IInteractionWorkItem>(metricsInstance)]),
            interactionProcessor ?? Substitute.For<IInteractionProcessor>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<InteractionQueueWorker>.Instance,
            metricsInstance);
        return new DiscordClientHost(
            logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DiscordClientHost>.Instance,
            new ConfigurationBuilder().Build().BotOptions(),
            producer,
            options,
            new SiteRegistry(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SiteRegistry>.Instance,
                new ConfigurationBuilder().Build().BotOptions(),
                services,
                []),
            interactions,
            interactionWorker,
            queueService,
            metricsInstance,
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

    private sealed class FakeWorkQueue : IWorkItemConsumer<MessageWorkItem>, IWorkItemProducer<MessageWorkItem>
    {
        private readonly Channel<WorkDelivery<MessageWorkItem>> _items = Channel.CreateUnbounded<WorkDelivery<MessageWorkItem>>();

        public List<string> Acknowledged { get; } = [];
        public Exception? AcknowledgeFailure { get; init; }
        public bool ReadCancellationObserved { get; private set; }
        public TaskCompletionSource ReadCancellationObservedSignal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int StartCalls { get; private set; }
        public int MaxProcessingAttempts { get; set; } = 3;
        public Action? OnStart { get; set; }
        public TimeSpan? LastEnqueueTimeout { get; private set; }

        public void Add(WorkDelivery<MessageWorkItem> delivery)
        {
            var lease = Substitute.For<IWorkItemLease>();
            lease.LostToken.Returns(CancellationToken.None);
            lease.CompleteAsync(Arg.Any<CancellationToken>())
                .Returns(call => CompleteAsync(delivery, call.Arg<CancellationToken>()));
            lease.RetryAsync(Arg.Any<Exception>(), Arg.Any<CancellationToken>())
                .Returns(call => RetryAsync(delivery, call.Arg<CancellationToken>()));
            lease.DisposeAsync().Returns(ValueTask.CompletedTask);
            _items.Writer.TryWrite(delivery with { Lease = lease });
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

        public async IAsyncEnumerable<WorkDelivery<MessageWorkItem>> ReadAsync(
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
                WorkDelivery<MessageWorkItem> item;
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

        public async IAsyncEnumerable<WorkDelivery<MessageWorkItem>> RecoverAsync(
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
            StartCalls++;
            OnStart?.Invoke();
            return Task.CompletedTask;
        }

        private Task<LeaseOperationResult> CompleteAsync(WorkDelivery<MessageWorkItem> item, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (AcknowledgeFailure is not null)
            {
                return Task.FromException<LeaseOperationResult>(AcknowledgeFailure);
            }

            Acknowledged.Add(item.DeliveryId);
            return Task.FromResult(LeaseOperationResult.Applied);
        }

        private Task<LeaseOperationResult> RetryAsync(WorkDelivery<MessageWorkItem> item, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return item.Attempt >= MaxProcessingAttempts
                ? CompleteAsync(item, cancellationToken)
                : Task.FromResult(LeaseOperationResult.Applied);
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

}
