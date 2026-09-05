using System.Diagnostics;
using SaucyBot.Diagnostics;

namespace SaucyBot.Queue;

public sealed class WorkQueueHostedService : BackgroundService, IAsyncDisposable
{
    private readonly IMessageWorkQueue _queue;
    private readonly IWorkItemProcessor _processor;
    private readonly WorkQueueOptions _options;
    private readonly ILogger<WorkQueueHostedService> _logger;
    private readonly InteractionWorkChannel _interactionChannel;
    private readonly IInteractionProcessor _interactionProcessor;
    private readonly ISaucyBotMetrics _metrics;
    private readonly List<Task> _workers = [];
    private readonly CancellationTokenSource _admissionCancellation = new();
    private readonly CancellationTokenSource _workerCancellation = new();
    private readonly CancellationTokenSource _readCancellation = new();
    private Task? _completion;

    public CancellationToken AdmissionToken => _admissionCancellation.Token;

    public WorkQueueHostedService(
        IMessageWorkQueue queue,
        IWorkItemProcessor processor,
        WorkQueueOptions options,
        ILogger<WorkQueueHostedService> logger,
        InteractionWorkChannel interactionChannel,
        IInteractionProcessor interactionProcessor,
        ISaucyBotMetrics metrics)
    {
        _queue = queue;
        _processor = processor;
        _options = options;
        _logger = logger;
        _interactionChannel = interactionChannel;
        _interactionProcessor = interactionProcessor;
        _metrics = metrics;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(_workerCancellation.Token);

        var count = Math.Max(1, _options.MessageWorkerCount);
        for (var i = 0; i < count; i++)
        {
            _workers.Add(RunWorkerAsync($"{Environment.MachineName}-{i}", linkedCancellation.Token));
        }

        for (var i = 0; i < Math.Max(1, _options.InteractionWorkerCount); i++)
        {
            _workers.Add(RunInteractionWorkerAsync(linkedCancellation.Token));
        }

        _completion = Task.WhenAll(_workers);
        _ = _completion.ContinueWith(
            completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        await _completion;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await _queue.ClearPendingAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        StopIntake();

        using var timeout = new CancellationTokenSource();
        timeout.CancelAfter(_options.ShutdownDrainTimeout);

        try
        {
            if (_completion is not null)
            {
                await _completion.WaitAsync(timeout.Token);
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            await _workerCancellation.CancelAsync();
        }

    }

    public void StopIntake()
    {
        if (_admissionCancellation.IsCancellationRequested)
        {
            return;
        }

        _admissionCancellation.Cancel();
        _readCancellation.Cancel();
        _interactionChannel?.Complete();
    }

    public ValueTask DisposeAsync()
    {
        _admissionCancellation.Dispose();
        _workerCancellation.Dispose();
        _readCancellation.Dispose();
        Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task RunWorkerAsync(string consumer, CancellationToken cancellationToken)
    {
        try
        {
            using var readCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _readCancellation.Token);

            await foreach (var item in _queue.ReadAsync(consumer, readCancellation.Token))
            {
                _metrics.Dequeued.Add(1);
                _metrics.QueueDepth.Add(-1);
                if (item.Item.EnqueuedAt != default)
                {
                    _metrics.QueueAge.Record((DateTimeOffset.UtcNow - item.Item.EnqueuedAt).TotalMilliseconds);
                }

                _metrics.ActiveWorkers.Add(1);
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    await _processor.ProcessAsync(item, cancellationToken);
                    await _queue.AcknowledgeAsync(item, cancellationToken);
                    _metrics.Succeeded.Add(1);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogDebug("Message worker {Consumer} cancelled while processing {EntryId}", consumer, item.EntryId);
                    _metrics.Cancelled.Add(1);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Message worker {Consumer} failed for {EntryId}", consumer, item.EntryId);
                    _metrics.Failed.Add(1);
                }
                finally
                {
                    stopwatch.Stop();
                    _metrics.ProcessingDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
                    _metrics.ActiveWorkers.Add(-1);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _readCancellation.IsCancellationRequested)
        {
            _logger.LogDebug(
                "Message worker {Consumer} stopped because queue consumption was cancelled",
                consumer);
        }
    }

    private async Task RunInteractionWorkerAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var interaction in _interactionChannel!.ReadAllAsync(cancellationToken))
            {
                _metrics.Dequeued.Add(1);
                _metrics.QueueDepth.Add(-1);
                _metrics.ActiveWorkers.Add(1);
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    await _interactionProcessor.ProcessAsync(interaction, cancellationToken);
                    _metrics.Succeeded.Add(1);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogDebug("Interaction worker cancelled");
                    _metrics.Cancelled.Add(1);
                    await SendInteractionFailureAsync(interaction, cancellationToken);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Interaction worker failed for {InteractionId}", interaction?.Id);
                    _metrics.Failed.Add(1);
                    await SendInteractionFailureAsync(interaction, cancellationToken);
                }
                finally
                {
                    stopwatch.Stop();
                    _metrics.ProcessingDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
                    _metrics.ActiveWorkers.Add(-1);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Interaction worker stopped because interaction consumption was cancelled");
        }
    }

    private async Task SendInteractionFailureAsync(IInteractionWorkItem? interaction, CancellationToken cancellationToken)
    {
        if (interaction is null)
        {
            _logger.LogError("Interaction processing failed before a follow-up could be sent");
            return;
        }

        try
        {
            await interaction.FollowupAsync("Failed to process this interaction.", ephemeral: true, cancellationToken);
        }
        catch (Exception followupException)
        {
            _logger.LogError(followupException, "Failed to send interaction failure response for {InteractionId}", interaction.Id);
        }
    }
}
