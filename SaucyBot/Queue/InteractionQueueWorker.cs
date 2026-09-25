using System.Diagnostics;
using SaucyBot.Diagnostics;

namespace SaucyBot.Queue;

public sealed class InteractionQueueWorker
{
    private static readonly TimeSpan FailureResponseTimeout = TimeSpan.FromSeconds(1);
    private readonly InteractionWorkChannel _channel;
    private readonly IQueueMiddlewarePipeline<IInteractionWorkItem> _pipeline;
    private readonly IInteractionProcessor _processor;
    private readonly ILogger<InteractionQueueWorker> _logger;
    private readonly ISaucyBotMetrics _metrics;

    public InteractionQueueWorker(
        InteractionWorkChannel channel,
        IQueueMiddlewarePipeline<IInteractionWorkItem> pipeline,
        IInteractionProcessor processor,
        ILogger<InteractionQueueWorker> logger,
        ISaucyBotMetrics metrics)
    {
        _channel = channel;
        _pipeline = pipeline;
        _processor = processor;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var interaction in _channel.ReadAllAsync(cancellationToken))
            {
                await ProcessOneAsync(interaction, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Interaction worker stopped because interaction consumption was cancelled");
        }
    }

    public async Task RunSupervisedAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RunAsync(cancellationToken);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Interaction worker failed; restarting");
                _metrics.WorkerRestarts.Add(1, new KeyValuePair<string, object?>("worker_type", "interaction"));
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }

    private async Task ProcessOneAsync(IInteractionWorkItem interaction, CancellationToken cancellationToken)
    {
        _metrics.Dequeued.Add(1);
        _metrics.QueueDepth.Add(-1);
        _metrics.ActiveWorkers.Add(1);
        using var activity = QueueTelemetry.ActivitySource.StartActivity(ActivityKind.Consumer);
        activity?.SetTag("saucybot.work.type", "interaction");
        activity?.SetTag("saucybot.interaction.id", interaction.Id);

        var context = new QueueWorkContext<IInteractionWorkItem>(
            interaction,
            interaction.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Attempt: 1,
            ReceivedAt: DateTimeOffset.UtcNow);

        try
        {
            await _pipeline.InvokeAsync(context, ProcessInteractionAsync, cancellationToken);
            _metrics.Succeeded.Add(1);
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _metrics.Cancelled.Add(1);
            activity?.SetTag("saucybot.cancelled", true);
            _logger.LogDebug("Interaction worker cancelled for interaction {InteractionId}", interaction.Id);
            await SendFailureResponseAsync(interaction);
        }
        catch (Exception exception)
        {
            _metrics.Failed.Add(1);
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity?.SetTag("error.type", exception.GetType().FullName);
            _logger.LogError(exception, "Interaction worker failed for {InteractionId}", interaction.Id);
            await SendFailureResponseAsync(interaction);
        }
        finally
        {
            _metrics.ActiveWorkers.Add(-1);
        }
    }

    private Task ProcessInteractionAsync(
        QueueWorkContext<IInteractionWorkItem> context,
        CancellationToken cancellationToken) =>
        _processor.ProcessAsync(context.Item, cancellationToken);

    private Task SendFailureResponseAsync(IInteractionWorkItem interaction) =>
        InteractionFailureResponder.SendAsync(
            interaction,
            _logger,
            FailureResponseTimeout);
}
