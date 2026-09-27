using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SaucyBot.Diagnostics;
using SaucyBot.Queue;
using SaucyBot.Tests.Unit.Common;
using Xunit;

namespace SaucyBot.Tests.Unit.Queue;

public sealed class InteractionQueueWorkerTest
{
    [Fact]
    public async Task MiddlewareWrapsSuccessfulInteractionProcessing()
    {
        var trace = new List<string>();
        var channel = CreateChannel();
        var interaction = new RecordingInteraction();
        var logger = new TestLogger<InteractionQueueWorker>();
        var processor = Substitute.For<IInteractionProcessor>();
        processor.ProcessAsync(Arg.Any<IInteractionWorkItem>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                trace.Add("terminal");
                return Task.CompletedTask;
            });
        var worker = CreateWorker(
            channel,
            processor,
            logger,
            new TraceMiddleware(trace));
        await channel.WriteAsync(interaction, TestContext.Current.CancellationToken);
        channel.Complete();

        await worker.RunSupervisedAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["enter", "terminal", "exit"], trace);
        Assert.Equal(0, interaction.InitialResponses);
        Assert.Equal(0, interaction.Followups);
        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Debug && entry.Message.Contains("starting queued interaction", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Debug && entry.Message.Contains("completed queued interaction", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ImmediateInteractionUsesPipelineWithoutChannelAdmission()
    {
        var trace = new List<string>();
        var channel = CreateChannel(capacity: 1);
        var interaction = new RecordingInteraction();
        var logger = new TestLogger<InteractionQueueWorker>();
        var processor = Substitute.For<IInteractionProcessor>();
        processor.ProcessAsync(Arg.Any<IInteractionWorkItem>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                trace.Add("terminal");
                return Task.CompletedTask;
            });
        var worker = CreateWorker(
            channel,
            processor,
            logger,
            new TraceMiddleware(trace));

        await worker.ProcessImmediatelyAsync(interaction, TestContext.Current.CancellationToken);

        Assert.Equal(["enter", "terminal", "exit"], trace);
        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Debug && entry.Message.Contains("starting immediate interaction", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MiddlewareFailureSendsFailureResponseAndReleasesChannelCapacity()
    {
        var channel = CreateChannel(capacity: 1);
        var first = new RecordingInteraction();
        var second = new RecordingInteraction();
        var processor = Substitute.For<IInteractionProcessor>();
        processor.ProcessAsync(Arg.Any<IInteractionWorkItem>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var worker = CreateWorker(
            channel,
            processor,
            new ThrowingMiddleware(new InvalidOperationException("middleware failed")));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var running = worker.RunSupervisedAsync(cancellation.Token);
        await channel.WriteAsync(first, cancellation.Token);
        await first.Responded.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await channel.WriteAsync(second, cancellation.Token).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await second.Responded.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        channel.Complete();
        await running.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(1, first.InitialResponses);
        Assert.Equal(1, second.InitialResponses);
        Assert.Equal("Failed to process this interaction.", first.ResponseContent);
    }

    [Fact]
    public async Task MiddlewareFailureAfterInitialResponseSendsOneFollowup()
    {
        var channel = CreateChannel();
        var interaction = new RecordingInteraction();
        var processor = Substitute.For<IInteractionProcessor>();
        processor.ProcessAsync(Arg.Any<IInteractionWorkItem>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IInteractionWorkItem>().RespondAsync(
                "initial response",
                ephemeral: true,
                call.Arg<CancellationToken>()));
        var worker = CreateWorker(
            channel,
            processor,
            new ThrowAfterNextMiddleware());
        await channel.WriteAsync(interaction, TestContext.Current.CancellationToken);
        channel.Complete();

        await worker.RunSupervisedAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, interaction.InitialResponses);
        Assert.Equal(1, interaction.Followups);
        Assert.Equal("followup", interaction.ResponseKind);
    }

    [Fact]
    public async Task ProcessingCancellationRecordsFailureResponseWithIndependentToken()
    {
        var channel = CreateChannel();
        var interaction = new RecordingInteraction();
        var processor = Substitute.For<IInteractionProcessor>();
        var processorStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        processor.ProcessAsync(interaction, Arg.Any<CancellationToken>()).Returns(call =>
        {
            var processingToken = call.Arg<CancellationToken>();
            processorStarted.TrySetResult();
            processingToken.Register(() => cancellationObserved.TrySetResult());
            return Task.Delay(Timeout.InfiniteTimeSpan, processingToken);
        });
        var trace = new List<string>();
        var worker = CreateWorker(channel, processor, new TraceMiddleware(trace));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var running = worker.RunSupervisedAsync(cancellation.Token);
        await channel.WriteAsync(interaction, TestContext.Current.CancellationToken);
        await processorStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await interaction.Responded.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        channel.Complete();
        await running.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await processor.Received(1).ProcessAsync(interaction, Arg.Any<CancellationToken>());
        Assert.Equal(["enter", "cancelled", "exit"], trace);
        Assert.Equal(1, interaction.InitialResponses);
        Assert.False(interaction.ResponseToken.IsCancellationRequested);
    }

    private static InteractionWorkChannel CreateChannel(int capacity = 2) =>
        new(new WorkQueueOptions { InteractionChannelCapacity = capacity });

    private static InteractionQueueWorker CreateWorker(
        InteractionWorkChannel channel,
        IInteractionProcessor processor,
        params IQueueMiddleware<IInteractionWorkItem>[] middleware) =>
        CreateWorker(channel, processor, NullLogger<InteractionQueueWorker>.Instance, middleware);

    private static InteractionQueueWorker CreateWorker(
        InteractionWorkChannel channel,
        IInteractionProcessor processor,
        ILogger<InteractionQueueWorker> logger,
        params IQueueMiddleware<IInteractionWorkItem>[] middleware) =>
        new(
            channel,
            new QueueMiddlewarePipeline<IInteractionWorkItem>(middleware),
            processor,
            logger,
            new SaucyBotMetrics());

    private sealed class RecordingInteraction : IInteractionWorkItem
    {
        public ulong Id => 10;
        public Discord.WebSocket.SocketInteraction? SocketInteraction => null;
        public bool IsSlashCommand => true;
        public string? CommandName => "settings";
        public bool HasResponded { get; private set; }
        public int InitialResponses { get; private set; }
        public int Followups { get; private set; }
        public string? ResponseKind { get; private set; }
        public string? ResponseContent { get; private set; }
        public CancellationToken ResponseToken { get; private set; }
        public TaskCompletionSource Responded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task DeferAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HasResponded = true;
            ResponseKind = "defer";
            return Task.CompletedTask;
        }

        public Task RespondAsync(
            string content,
            bool ephemeral,
            CancellationToken cancellationToken = default,
            TimeSpan? timeout = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HasResponded = true;
            InitialResponses++;
            ResponseContent = content;
            ResponseToken = cancellationToken;
            ResponseKind = "initial";
            Responded.TrySetResult();
            return Task.CompletedTask;
        }

        public Task FollowupAsync(
            string content,
            bool ephemeral,
            CancellationToken cancellationToken = default,
            TimeSpan? timeout = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HasResponded = true;
            Followups++;
            ResponseContent = content;
            ResponseToken = cancellationToken;
            ResponseKind = "followup";
            Responded.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class TraceMiddleware(List<string> trace) : IQueueMiddleware<IInteractionWorkItem>
    {
        public async Task InvokeAsync(
            QueueWorkContext<IInteractionWorkItem> context,
            QueueWorkDelegate<IInteractionWorkItem> next,
            CancellationToken cancellationToken)
        {
            trace.Add("enter");
            try
            {
                await next(context, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                trace.Add("cancelled");
                throw;
            }
            finally
            {
                trace.Add("exit");
            }
        }
    }

    private sealed class ThrowingMiddleware(Exception failure) : IQueueMiddleware<IInteractionWorkItem>
    {
        public Task InvokeAsync(
            QueueWorkContext<IInteractionWorkItem> context,
            QueueWorkDelegate<IInteractionWorkItem> next,
            CancellationToken cancellationToken) =>
            Task.FromException(failure);
    }

    private sealed class ThrowAfterNextMiddleware : IQueueMiddleware<IInteractionWorkItem>
    {
        public async Task InvokeAsync(
            QueueWorkContext<IInteractionWorkItem> context,
            QueueWorkDelegate<IInteractionWorkItem> next,
            CancellationToken cancellationToken)
        {
            await next(context, cancellationToken);
            throw new InvalidOperationException("post-processing failed");
        }
    }
}
