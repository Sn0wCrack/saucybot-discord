using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SaucyBot;
using SaucyBot.Database;
using SaucyBot.Diagnostics;
using SaucyBot.Library.Discord;
using SaucyBot.Queue;
using SaucyBot.Services;
using Xunit;

namespace SaucyBot.Tests.Unit;

public sealed class InteractionResponseTest
{
    [Fact]
    public async Task FailedInteractionReceivesATerminalInitialResponse()
    {
        await using var fixture = CreateFixture(() => throw new InvalidOperationException("failed"));
        var interaction = new RecordingInteraction { CommandName = "settings" };

        await fixture.Service.StartAsync(TestContext.Current.CancellationToken);
        await fixture.ClientHost.AdmitInteractionAsync(interaction);
        await interaction.ResponseSent.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await fixture.Service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal("initial", interaction.ResponseKind);
        Assert.Equal("Failed to process this interaction.", interaction.ResponseContent);
    }

    [Fact]
    public async Task FailedAlreadyRespondedInteractionReceivesAFollowup()
    {
        await using var fixture = CreateFixture(() => throw new InvalidOperationException("failed"));
        var interaction = new RecordingInteraction { HasResponded = true };

        await fixture.Service.StartAsync(TestContext.Current.CancellationToken);
        await fixture.ClientHost.AdmitInteractionAsync(interaction);
        await interaction.ResponseSent.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await fixture.Service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal("followup", interaction.ResponseKind);
    }

    [Fact]
    public async Task FailureResponseUsesAnIndependentResponseToken()
    {
        var interaction = new RecordingInteraction { HasResponded = true };
        using var processingCancellation = new CancellationTokenSource();
        processingCancellation.Cancel();

        var sent = await InteractionFailureResponder.SendAsync(
            interaction,
            NullLogger<Worker>.Instance,
            TimeSpan.FromSeconds(1),
            processingCancellation.Token);

        Assert.True(sent);
        Assert.False(interaction.LastResponseCancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task SocketInteractionFollowupHonorsCancellationBeforeSending()
    {
        var interaction = (Discord.WebSocket.SocketInteraction)RuntimeHelpers.GetUninitializedObject(
            typeof(Discord.WebSocket.SocketSlashCommand));
        var item = new SocketInteractionWorkItem(interaction);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            item.FollowupAsync("failure", ephemeral: true, cancellation.Token));
    }

    private static Fixture CreateFixture(Action process)
    {
        var queue = new EmptyWorkQueue();
        var options = new WorkQueueOptions
        {
            MessageWorkerCount = 1,
            InteractionWorkerCount = 1,
            ShutdownDrainTimeout = TimeSpan.FromMilliseconds(100),
        };
        var deliveries = new MessageDeliveryChannel(options);
        var channel = new InteractionWorkChannel(options);
        var metrics = new SaucyBotMetrics();
        var pipeline = new QueueMiddlewarePipeline<MessageWorkItem>([]);
        var service = new WorkQueueHostedService(
            queue,
            deliveries,
            new MessageQueueReader(queue, deliveries, NullLogger<MessageQueueReader>.Instance),
            new MessageRecoveryWorker(queue, deliveries, options, NullLogger<MessageRecoveryWorker>.Instance, metrics),
            new MessageQueueWorker(deliveries, pipeline, Substitute.For<IWorkItemProcessor>(), options, NullLogger<MessageQueueWorker>.Instance, metrics),
            options,
            NullLogger<WorkQueueHostedService>.Instance,
            channel,
            new CallbackInteractionProcessor(process),
            metrics);
        var services = new ServiceCollection().BuildServiceProvider();
        var clientHost = new DiscordClientHost(
            NullLogger<DiscordClientHost>.Instance,
            new ConfigurationBuilder().Build().BotOptions(),
            queue,
            options,
            new SiteRegistry(NullLogger<SiteRegistry>.Instance, new ConfigurationBuilder().Build().BotOptions(), services, []),
            channel,
            new CallbackInteractionProcessor(process),
            service,
            new SaucyBotMetrics(),
            new InteractionHandler(NullLogger<InteractionHandler>.Instance, services),
            Substitute.For<IMessageResolver>());

        return new Fixture(service, clientHost);
    }

    private sealed record Fixture(WorkQueueHostedService Service, DiscordClientHost ClientHost) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Service.DisposeAsync();
    }

    private sealed class CallbackInteractionProcessor(Action callback) : IInteractionProcessor
    {
        public Task ProcessAsync(IInteractionWorkItem interaction, CancellationToken cancellationToken)
        {
            callback();
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingInteraction : IInteractionWorkItem
    {
        public ulong Id => 42;
        public Discord.WebSocket.SocketInteraction? SocketInteraction => null;
        public bool IsSlashCommand => true;
        public string? CommandName { get; init; } = "sauce";
        public bool HasResponded { get; set; }
        public string? ResponseContent { get; private set; }
        public string? ResponseKind { get; private set; }
        public CancellationToken LastResponseCancellationToken { get; private set; }
        public TaskCompletionSource ResponseSent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task DeferAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HasResponded = true;
            ResponseKind = "defer";
            return Task.CompletedTask;
        }

        public Task RespondAsync(string content, bool ephemeral, CancellationToken cancellationToken = default, TimeSpan? timeout = null)
        {
            HasResponded = true;
            ResponseContent = content;
            ResponseKind = "initial";
            LastResponseCancellationToken = cancellationToken;
            ResponseSent.TrySetResult();
            return Task.CompletedTask;
        }

        public Task FollowupAsync(string content, bool ephemeral, CancellationToken cancellationToken = default, TimeSpan? timeout = null)
        {
            ResponseContent = content;
            ResponseKind = "followup";
            LastResponseCancellationToken = cancellationToken;
            ResponseSent.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class EmptyWorkQueue : IWorkItemProducer<MessageWorkItem>, IWorkItemConsumer<MessageWorkItem>
    {
        public Task<EnqueueResult> EnqueueAsync(
            MessageWorkItem item,
            TimeSpan timeout,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Interaction response tests do not enqueue message work.");

        public async IAsyncEnumerable<WorkDelivery<MessageWorkItem>> ReadAsync(
            string consumer,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            yield break;
        }

        public async IAsyncEnumerable<WorkDelivery<MessageWorkItem>> RecoverAsync(
            string consumer,
            TimeSpan minimumIdleTime,
            int count,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
