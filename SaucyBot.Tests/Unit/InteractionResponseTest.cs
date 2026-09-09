using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
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
        var channel = new InteractionWorkChannel(new WorkQueueOptions());
        var service = new WorkQueueHostedService(
            queue,
            Substitute.For<IWorkItemProcessor>(),
            new WorkQueueOptions { InteractionWorkerCount = 1, ShutdownDrainTimeout = TimeSpan.FromMilliseconds(100) },
            NullLogger<WorkQueueHostedService>.Instance,
            channel,
            new CallbackInteractionProcessor(process),
            new SaucyBotMetrics());
        var services = new ServiceCollection().BuildServiceProvider();
        var clientHost = new DiscordClientHost(
            NullLogger<DiscordClientHost>.Instance,
            new ConfigurationBuilder().Build().BotOptions(),
            queue,
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

    private sealed class EmptyWorkQueue : IMessageWorkQueue
    {
        private readonly Channel<QueuedMessageWorkItem> _items = Channel.CreateUnbounded<QueuedMessageWorkItem>();

        public Task EnqueueAsync(MessageWorkItem item, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Interaction response tests do not enqueue message work.");

        public async IAsyncEnumerable<QueuedMessageWorkItem> ReadAsync(
            string consumer,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            while (true)
            {
                yield return await _items.Reader.ReadAsync(cancellationToken);
            }
        }

        public Task AcknowledgeAsync(QueuedMessageWorkItem item, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Interaction response tests do not acknowledge message work.");

        public Task DeleteAsync(string entryId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Interaction response tests do not delete message work.");

        public Task ClearPendingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
