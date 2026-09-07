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
    private readonly TaskCompletionSource _workersReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _completion;
    private int _disposed;

    public CancellationToken AdmissionToken => _admissionCancellation.Token;
    public Task WorkerCompletion => _completion ?? Task.CompletedTask;

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
        using var stoppingRegistration = stoppingToken.Register(StopIntake);
        var workerCancellation = _workerCancellation.Token;

        var messageWorkers = Math.Max(1, _options.MessageWorkerCount);
        var interactionWorkers = Math.Max(1, _options.InteractionWorkerCount);

        for (var i = 0; i < messageWorkers; i++)
        {
            _workers.Add(RunWorkerAsync($"{Environment.MachineName}-{i}", workerCancellation));
        }

        for (var i = 0; i < interactionWorkers; i++)
        {
            _workers.Add(RunInteractionWorkerAsync(workerCancellation));
        }

        _logger.LogInformation(
            "Queue workers started with {MessageWorkerCount} message workers and {InteractionWorkerCount} interaction workers",
            messageWorkers,
            interactionWorkers
        );

        _completion = Task.WhenAll(_workers);
        _workersReady.TrySetResult();
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
        _logger.LogInformation("Stopping queue workers and draining admitted work");
        StopIntake();
        if (cancellationToken.IsCancellationRequested)
        {
            _workerCancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
        }

        using var timeout = new CancellationTokenSource();
        timeout.CancelAfter(_options.ShutdownDrainTimeout);

        try
        {
            if (_completion is not null)
            {
                using var drainCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    timeout.Token,
                    cancellationToken);
                await _completion.WaitAsync(drainCancellation.Token);
            }

            _logger.LogInformation("Queue workers drained admitted work successfully");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Queue worker drain was cancelled by the host caller");
            _workerCancellation.Cancel();
            throw;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Queue worker drain exceeded {ShutdownDrainTimeout}; cancelling remaining work",
                _options.ShutdownDrainTimeout);
            _workerCancellation.Cancel();
        }

    }

    public void StopIntake()
    {
        _admissionCancellation.Cancel();
        _readCancellation.Cancel();
        _interactionChannel.Complete();
    }

    public ValueTask DisposeAsync()
    {
        if (_completion is { IsCompleted: false } completion)
        {
            _ = completion.ContinueWith(
                _ => DisposeResources(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        else
        {
            DisposeResources();
        }

        return ValueTask.CompletedTask;
    }

    private void DisposeResources()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Dispose();
        _admissionCancellation.Dispose();
        _workerCancellation.Dispose();
        _readCancellation.Dispose();
    }

    private async Task RunWorkerAsync(string consumer, CancellationToken cancellationToken)
    {
        try
        {
            await _workersReady.Task.WaitAsync(cancellationToken);
            using var readCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _readCancellation.Token);

            await foreach (var item in _queue.ReadAsync(consumer, readCancellation.Token))
            {
                _metrics.Dequeued.Add(1);
                _metrics.QueueDepth.Add(-1);
                _logger.LogDebug(
                    "Message worker {Consumer} picked up queue entry {EntryId}",
                    consumer,
                    item.EntryId);
                if (item.Item.EnqueuedAt != default)
                {
                    _metrics.QueueAge.Record((DateTimeOffset.UtcNow - item.Item.EnqueuedAt).TotalMilliseconds);
                }

                _metrics.ActiveWorkers.Add(1);
                using var activity = QueueTelemetry.ActivitySource.StartActivity(ActivityKind.Consumer);
                activity?.SetTag("saucybot.work.type", "message");
                activity?.SetTag("saucybot.queue.consumer", consumer);
                activity?.SetTag("saucybot.queue.entry_id", item.EntryId);
                try
                {
                    await _processor.ProcessAsync(item, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    await _queue.AcknowledgeAsync(item, CancellationToken.None);
                    _metrics.Succeeded.Add(1);
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    _logger.LogDebug(
                        "Message worker {Consumer} completed queue entry {EntryId}",
                        consumer,
                        item.EntryId);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogDebug("Message worker {Consumer} cancelled while processing {EntryId}", consumer, item.EntryId);
                    _metrics.Cancelled.Add(1);
                    activity?.SetTag("saucybot.cancelled", true);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Message worker {Consumer} failed for {EntryId}", consumer, item.EntryId);
                    _metrics.Failed.Add(1);
                    activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
                    activity?.SetTag("error.type", exception.GetType().FullName);
                }
                finally
                {
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
            await _workersReady.Task.WaitAsync(cancellationToken);
            await foreach (var interaction in _interactionChannel!.ReadAllAsync(cancellationToken))
            {
                _metrics.Dequeued.Add(1);
                _metrics.QueueDepth.Add(-1);
                _metrics.ActiveWorkers.Add(1);
                _logger.LogDebug("Interaction worker picked up interaction {InteractionId}", interaction?.Id);
                using var activity = QueueTelemetry.ActivitySource.StartActivity(ActivityKind.Consumer);
                activity?.SetTag("saucybot.work.type", "interaction");
                activity?.SetTag("saucybot.interaction.id", interaction?.Id);
                try
                {
                    await _interactionProcessor.ProcessAsync(interaction!, cancellationToken);
                    _metrics.Succeeded.Add(1);
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    _logger.LogDebug("Interaction worker completed interaction {InteractionId}", interaction?.Id);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogDebug("Interaction worker cancelled");
                    _metrics.Cancelled.Add(1);
                    activity?.SetTag("saucybot.cancelled", true);
                    await SendInteractionFailureAsync(interaction);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Interaction worker failed for {InteractionId}", interaction?.Id);
                    _metrics.Failed.Add(1);
                    activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
                    activity?.SetTag("error.type", exception.GetType().FullName);
                    await SendInteractionFailureAsync(interaction);
                }
                finally
                {
                    _metrics.ActiveWorkers.Add(-1);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Interaction worker stopped because interaction consumption was cancelled");
        }
    }

    private Task SendInteractionFailureAsync(IInteractionWorkItem? interaction)
    {
        if (interaction is null)
        {
            _logger.LogError("Interaction processing failed before a failure response could be sent");
            return Task.CompletedTask;
        }

        return InteractionFailureResponder.SendAsync(
            interaction,
            _logger,
            TimeSpan.FromSeconds(1));
    }
}
