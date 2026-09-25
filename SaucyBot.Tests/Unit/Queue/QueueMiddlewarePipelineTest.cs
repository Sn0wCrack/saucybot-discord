using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SaucyBot.Diagnostics;
using SaucyBot.Queue;
using SaucyBot.Tests.Unit.Common;
using Xunit;

namespace SaucyBot.Tests.Unit.Queue;

public sealed class QueueMiddlewarePipelineTest
{
    private static readonly DateTimeOffset ReceivedAt = new(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);

    private readonly List<string> _trace = [];

    private int TerminalCalls { get; set; }
    private QueueWorkContext<MessageWorkItem>? TerminalContext { get; set; }
    private CancellationToken TerminalToken { get; set; }
    private Exception? TerminalFailure { get; set; }

    [Fact]
    public async Task PipelineRunsMiddlewareInRegistrationOrderAndUnwindsInReverse()
    {
        var first = new RecordingMiddleware("first", _trace);
        var second = new RecordingMiddleware("second", _trace);
        var third = new RecordingMiddleware("third", _trace);
        var pipeline = CreatePipeline(first, second, third);

        await pipeline.InvokeAsync(CreateContext(), TerminalAsync, CancellationToken.None);

        Assert.Equal(
            ["enter:first", "enter:second", "enter:third", "terminal", "exit:third", "exit:second", "exit:first"],
            _trace);
        Assert.Equal(1, TerminalCalls);
    }

    [Fact]
    public async Task PipelinePassesTheSameContextAndCancellationTokenToEveryStep()
    {
        var first = new RecordingMiddleware("first", _trace);
        var second = new RecordingMiddleware("second", _trace);
        var pipeline = CreatePipeline(first, second);
        var context = CreateContext();
        using var cancellation = new CancellationTokenSource();

        await pipeline.InvokeAsync(context, TerminalAsync, cancellation.Token);

        Assert.Same(context, first.Contexts[0]);
        Assert.Same(context, second.Contexts[0]);
        Assert.Same(context, TerminalContext);
        Assert.Equal(cancellation.Token, first.Tokens[0]);
        Assert.Equal(cancellation.Token, second.Tokens[0]);
        Assert.Equal(cancellation.Token, TerminalToken);
        Assert.Equal(context.DeliveryId, TerminalContext!.DeliveryId);
        Assert.Equal(context.Attempt, TerminalContext!.Attempt);
        Assert.Equal(context.ReceivedAt, TerminalContext!.ReceivedAt);
    }

    [Fact]
    public async Task PipelineSupportsInteractionWorkContext()
    {
        var context = new QueueWorkContext<IInteractionWorkItem>(
            Substitute.For<IInteractionWorkItem>(),
            "interaction-2",
            3,
            ReceivedAt);

        var interactionPipeline = new QueueMiddlewarePipeline<IInteractionWorkItem>(
            Array.Empty<IQueueMiddleware<IInteractionWorkItem>>());
        await interactionPipeline.InvokeAsync(context, InteractionTerminalAsync, CancellationToken.None);

        Assert.Equal("interaction-2", context.DeliveryId);
        Assert.Equal(3, context.Attempt);
    }

    [Fact]
    public async Task PipelineWithoutMiddlewareCallsTheTerminalOnce()
    {
        var pipeline = CreatePipeline();

        await pipeline.InvokeAsync(CreateContext(), TerminalAsync, CancellationToken.None);

        Assert.Equal(1, TerminalCalls);
        Assert.Equal(["terminal"], _trace);
    }

    [Fact]
    public async Task MiddlewareFailureBeforeNextStopsTheChainAndPropagates()
    {
        var first = new RecordingMiddleware("first", _trace);
        var second = new RecordingMiddleware("second", _trace)
        {
            FailBeforeNext = new InvalidOperationException("before next"),
        };
        var third = new RecordingMiddleware("third", _trace);
        var pipeline = CreatePipeline(first, second, third);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => pipeline.InvokeAsync(CreateContext(), TerminalAsync, CancellationToken.None));

        Assert.Equal("before next", exception.Message);
        Assert.Equal(["enter:first", "enter:second", "exit:second", "exit:first"], _trace);
        Assert.Equal(0, TerminalCalls);
        Assert.Empty(third.Contexts);
    }

    [Fact]
    public async Task MiddlewareFailureAfterNextPropagatesAfterInnerStepsRun()
    {
        var first = new RecordingMiddleware("first", _trace);
        var second = new RecordingMiddleware("second", _trace)
        {
            FailAfterNext = new InvalidOperationException("after next"),
        };
        var third = new RecordingMiddleware("third", _trace);
        var pipeline = CreatePipeline(first, second, third);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => pipeline.InvokeAsync(CreateContext(), TerminalAsync, CancellationToken.None));

        Assert.Equal("after next", exception.Message);
        Assert.Equal(
            ["enter:first", "enter:second", "enter:third", "terminal", "exit:third", "exit:second", "exit:first"],
            _trace);
        Assert.Equal(1, TerminalCalls);
    }

    [Fact]
    public async Task TerminalFailurePropagatesThroughAllMiddleware()
    {
        TerminalFailure = new InvalidOperationException("terminal failed");
        var first = new RecordingMiddleware("first", _trace);
        var second = new RecordingMiddleware("second", _trace);
        var third = new RecordingMiddleware("third", _trace);
        var pipeline = CreatePipeline(first, second, third);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => pipeline.InvokeAsync(CreateContext(), TerminalAsync, CancellationToken.None));

        Assert.Same(TerminalFailure, exception);
        Assert.Equal(
            ["enter:first", "enter:second", "enter:third", "terminal", "exit:third", "exit:second", "exit:first"],
            _trace);
        Assert.Equal(1, TerminalCalls);
    }

    [Fact]
    public async Task MiddlewareCallingNextTwiceHitsTheSingleUseGuard()
    {
        var middleware = new RecordingMiddleware("first", _trace) { CallNextTwice = true };
        var pipeline = CreatePipeline(middleware);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => pipeline.InvokeAsync(CreateContext(), TerminalAsync, CancellationToken.None));

        Assert.Equal("Queue middleware called next more than once.", exception.Message);
        Assert.Equal(1, TerminalCalls);
    }

    [Fact]
    public async Task MiddlewareThatSkipsNextShortCircuitsTheTerminal()
    {
        var first = new RecordingMiddleware("first", _trace);
        var second = new RecordingMiddleware("second", _trace) { SkipNext = true };
        var third = new RecordingMiddleware("third", _trace);
        var pipeline = CreatePipeline(first, second, third);

        await pipeline.InvokeAsync(CreateContext(), TerminalAsync, CancellationToken.None);

        Assert.Equal(["enter:first", "enter:second", "exit:second", "exit:first"], _trace);
        Assert.Equal(0, TerminalCalls);
        Assert.Empty(third.Contexts);
    }

    [Fact]
    public async Task CancellationFromTheTerminalPropagatesToTheCaller()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        TerminalFailure = new OperationCanceledException(cancellation.Token);
        var middleware = new RecordingMiddleware("first", _trace);
        var pipeline = CreatePipeline(middleware);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pipeline.InvokeAsync(CreateContext(), TerminalAsync, cancellation.Token));

        Assert.Equal(["enter:first", "terminal", "exit:first"], _trace);
    }

    [Fact]
    public async Task MetricsMiddlewareRecordsSuccessOutcomeAndProcessingDuration()
    {
        using var metrics = new SaucyBotMetrics();
        using var capture = new MetricsCapture();
        var pipeline = CreatePipeline(new QueueMetricsMiddleware<MessageWorkItem>(metrics));

        await pipeline.InvokeAsync(CreateContext(), TerminalAsync, CancellationToken.None);

        var outcome = Assert.Single(capture.Samples, sample => sample.Name == "saucybot.queue.handler_outcomes");
        Assert.Equal(1, outcome.Value);
        Assert.Equal(2, outcome.Tags.Count);
        Assert.Equal("message", outcome.Tags["work_type"]);
        Assert.Equal("succeeded", outcome.Tags["outcome"]);

        var duration = Assert.Single(capture.Samples, sample => sample.Name == "saucybot.queue.handler_duration");
        Assert.True(duration.Value >= 0);
        Assert.Single(duration.Tags);
        Assert.Equal("message", duration.Tags["work_type"]);
    }

    [Fact]
    public async Task MetricsMiddlewareRecordsFailureOutcomeAndRethrows()
    {
        using var metrics = new SaucyBotMetrics();
        using var capture = new MetricsCapture();
        TerminalFailure = new InvalidOperationException("handler failed");
        var pipeline = CreatePipeline(new QueueMetricsMiddleware<MessageWorkItem>(metrics));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => pipeline.InvokeAsync(CreateContext(), TerminalAsync, CancellationToken.None));

        var outcome = Assert.Single(capture.Samples, sample => sample.Name == "saucybot.queue.handler_outcomes");
        Assert.Equal(1, outcome.Value);
        Assert.Equal("message", outcome.Tags["work_type"]);
        Assert.Equal("failed", outcome.Tags["outcome"]);
    }

    [Fact]
    public async Task MetricsMiddlewareRecordsCancellationOutcomeAndRethrows()
    {
        using var metrics = new SaucyBotMetrics();
        using var capture = new MetricsCapture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        TerminalFailure = new OperationCanceledException(cancellation.Token);
        var pipeline = CreatePipeline(new QueueMetricsMiddleware<MessageWorkItem>(metrics));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pipeline.InvokeAsync(CreateContext(), TerminalAsync, cancellation.Token));

        var outcome = Assert.Single(capture.Samples, sample => sample.Name == "saucybot.queue.handler_outcomes");
        Assert.Equal(1, outcome.Value);
        Assert.Equal("message", outcome.Tags["work_type"]);
        Assert.Equal("cancelled", outcome.Tags["outcome"]);
    }

    [Fact]
    public async Task MetricsMiddlewareClassifiesUnrequestedCancellationAsFailure()
    {
        using var metrics = new SaucyBotMetrics();
        using var capture = new MetricsCapture();
        TerminalFailure = new OperationCanceledException(CancellationToken.None);
        var pipeline = CreatePipeline(new QueueMetricsMiddleware<MessageWorkItem>(metrics));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pipeline.InvokeAsync(CreateContext(), TerminalAsync, CancellationToken.None));

        var outcome = Assert.Single(capture.Samples, sample => sample.Name == "saucybot.queue.handler_outcomes");
        Assert.Equal("failed", outcome.Tags["outcome"]);
    }

    [Fact]
    public void MiddlewareContextDoesNotExposeLeaseOperations()
    {
        var properties = typeof(QueueWorkContext<MessageWorkItem>).GetProperties();

        Assert.DoesNotContain(
            properties,
            property => typeof(IWorkItemLease).IsAssignableFrom(property.PropertyType));
    }

    [Fact]
    public async Task MetricsMiddlewareLabelsInteractionWorkAsInteraction()
    {
        using var metrics = new SaucyBotMetrics();
        using var capture = new MetricsCapture();
        var pipeline = new QueueMiddlewarePipeline<IInteractionWorkItem>(
            new IQueueMiddleware<IInteractionWorkItem>[] { new QueueMetricsMiddleware<IInteractionWorkItem>(metrics) });
        var context = new QueueWorkContext<IInteractionWorkItem>(
            Substitute.For<IInteractionWorkItem>(),
            "interaction-1",
            1,
            ReceivedAt);

        await pipeline.InvokeAsync(context, InteractionTerminalAsync, CancellationToken.None);

        var outcome = Assert.Single(capture.Samples, sample => sample.Name == "saucybot.queue.handler_outcomes");
        Assert.Equal(1, outcome.Value);
        Assert.Equal("interaction", outcome.Tags["work_type"]);
        Assert.Equal("succeeded", outcome.Tags["outcome"]);
    }

    [Fact]
    public async Task ModulesRegisterMiddlewareInRegistrationOrderWithoutEditingThePipeline()
    {
        var sink = new TraceSink(_trace);
        var services = new ServiceCollection();
        services.AddSingleton(sink);
        services.AddQueueMiddleware<MessageWorkItem, FirstModuleMiddleware>();
        services.AddQueueMiddleware<MessageWorkItem, SecondModuleMiddleware>();

        await using var provider = services.BuildServiceProvider();
        var pipeline = provider.GetRequiredService<IQueueMiddlewarePipeline<MessageWorkItem>>();

        await pipeline.InvokeAsync(CreateContext(), TerminalAsync, CancellationToken.None);

        Assert.Equal(["enter:first", "enter:second", "terminal", "exit:second", "exit:first"], sink.Entries);
        Assert.Same(pipeline, provider.GetRequiredService<IQueueMiddlewarePipeline<MessageWorkItem>>());
    }

    [Fact]
    public void AddQueueMiddlewareRegistersTheCommonMetricsMiddleware()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISaucyBotMetrics, SaucyBotMetrics>();
        services.AddQueueMiddleware<MessageWorkItem, QueueMetricsMiddleware<MessageWorkItem>>();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<QueueMetricsMiddleware<MessageWorkItem>>(
            provider.GetRequiredService<IQueueMiddleware<MessageWorkItem>>());
    }

    private static QueueMiddlewarePipeline<MessageWorkItem> CreatePipeline(
        params IQueueMiddleware<MessageWorkItem>[] middleware) =>
        new(middleware);

    private static QueueWorkContext<MessageWorkItem> CreateContext() => new(
        TestData.Message(),
        "delivery-1",
        2,
        ReceivedAt);

    private Task TerminalAsync(QueueWorkContext<MessageWorkItem> context, CancellationToken cancellationToken)
    {
        TerminalCalls++;
        TerminalContext = context;
        TerminalToken = cancellationToken;
        _trace.Add("terminal");

        return TerminalFailure is null
            ? Task.CompletedTask
            : Task.FromException(TerminalFailure);
    }

    private static Task InteractionTerminalAsync(
        QueueWorkContext<IInteractionWorkItem> context,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;

    private sealed class RecordingMiddleware(
        string name,
        List<string> trace) : IQueueMiddleware<MessageWorkItem>
    {
        public List<QueueWorkContext<MessageWorkItem>> Contexts { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];
        public Exception? FailBeforeNext { get; init; }
        public Exception? FailAfterNext { get; init; }
        public bool SkipNext { get; init; }
        public bool CallNextTwice { get; init; }

        public async Task InvokeAsync(
            QueueWorkContext<MessageWorkItem> context,
            QueueWorkDelegate<MessageWorkItem> next,
            CancellationToken cancellationToken)
        {
            Contexts.Add(context);
            Tokens.Add(cancellationToken);
            trace.Add($"enter:{name}");

            try
            {
                if (FailBeforeNext is not null)
                {
                    throw FailBeforeNext;
                }

                if (!SkipNext)
                {
                    await next(context, cancellationToken);
                    if (CallNextTwice)
                    {
                        await next(context, cancellationToken);
                    }
                }

                if (FailAfterNext is not null)
                {
                    throw FailAfterNext;
                }
            }
            finally
            {
                trace.Add($"exit:{name}");
            }
        }
    }

    private sealed class TraceSink(List<string> entries)
    {
        public List<string> Entries { get; } = entries;
    }

    private sealed class FirstModuleMiddleware(TraceSink sink) : IQueueMiddleware<MessageWorkItem>
    {
        public async Task InvokeAsync(
            QueueWorkContext<MessageWorkItem> context,
            QueueWorkDelegate<MessageWorkItem> next,
            CancellationToken cancellationToken)
        {
            sink.Entries.Add("enter:first");
            await next(context, cancellationToken);
            sink.Entries.Add("exit:first");
        }
    }

    private sealed class SecondModuleMiddleware(TraceSink sink) : IQueueMiddleware<MessageWorkItem>
    {
        public async Task InvokeAsync(
            QueueWorkContext<MessageWorkItem> context,
            QueueWorkDelegate<MessageWorkItem> next,
            CancellationToken cancellationToken)
        {
            sink.Entries.Add("enter:second");
            await next(context, cancellationToken);
            sink.Entries.Add("exit:second");
        }
    }

    private sealed class MetricsCapture : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly object _gate = new();
        private readonly List<MetricSample> _samples = [];

        public MetricsCapture()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == SaucyBotMetrics.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>(RecordLong);
            _listener.SetMeasurementEventCallback<double>(RecordDouble);
            _listener.Start();
        }

        public IReadOnlyList<MetricSample> Samples
        {
            get
            {
                lock (_gate)
                {
                    return _samples.ToArray();
                }
            }
        }

        public void Dispose() => _listener.Dispose();

        private void RecordLong(
            Instrument instrument,
            long measurement,
            ReadOnlySpan<KeyValuePair<string, object?>> tags,
            object? state) =>
            Record(instrument, measurement, tags);

        private void RecordDouble(
            Instrument instrument,
            double measurement,
            ReadOnlySpan<KeyValuePair<string, object?>> tags,
            object? state) =>
            Record(instrument, measurement, tags);

        private void Record(Instrument instrument, double measurement, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var captured = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var tag in tags)
            {
                captured[tag.Key] = tag.Value;
            }

            lock (_gate)
            {
                _samples.Add(new MetricSample(instrument.Name, measurement, captured));
            }
        }
    }

    private sealed record MetricSample(string Name, double Value, Dictionary<string, object?> Tags);
}
