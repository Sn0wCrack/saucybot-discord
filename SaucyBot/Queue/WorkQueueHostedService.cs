using System.Diagnostics;
using Discord.WebSocket;
using SaucyBot.Diagnostics;
using SaucyBot.Services;

namespace SaucyBot.Queue;

public sealed class WorkQueueHostedService : BackgroundService, IAsyncDisposable
{
    private readonly IMessageWorkQueue _queue;
    private readonly IWorkItemProcessor _processor;
    private readonly WorkQueueOptions _options;
    private readonly ILogger<WorkQueueHostedService> _logger;
    private readonly InteractionWorkChannel? _interactionChannel;
    private readonly InteractionHandler? _interactionHandler;
    private readonly IServiceProvider? _services;
    private readonly SaucyBotMetrics? _metrics;
    private readonly Func<SocketInteraction, CancellationToken, Task>? _interactionProcessor;
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
        InteractionWorkChannel? interactionChannel = null,
        InteractionHandler? interactionHandler = null,
        IServiceProvider? services = null,
        SaucyBotMetrics? metrics = null,
        Func<SocketInteraction, CancellationToken, Task>? interactionProcessor = null)
    {
        _queue = queue;
        _processor = processor;
        _options = options;
        _logger = logger;
        _interactionChannel = interactionChannel;
        _interactionHandler = interactionHandler;
        _services = services;
        _metrics = metrics;
        _interactionProcessor = interactionProcessor;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            stoppingToken,
            _workerCancellation.Token);

        var count = Math.Max(1, _options.MessageWorkerCount);
        for (var i = 0; i < count; i++)
        {
            _workers.Add(RunWorkerAsync($"{Environment.MachineName}-{i}", linkedCancellation.Token));
        }

        if (_interactionChannel is not null && (_interactionProcessor is not null
            || (_interactionHandler is not null && _services is not null)))
        {
            for (var i = 0; i < Math.Max(1, _options.InteractionWorkerCount); i++)
            {
                _workers.Add(RunInteractionWorkerAsync(linkedCancellation.Token));
            }
        }

        _completion = Task.WhenAll(_workers);
        await _completion;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        StopIntake();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
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
            _workerCancellation.Cancel();
        }

        await base.StopAsync(cancellationToken);
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
                _metrics?.Dequeued.Add(1);
                _metrics?.QueueDepth.Add(-1);
                if (item.Item.EnqueuedAt != default)
                {
                    _metrics?.QueueAge.Record((DateTimeOffset.UtcNow - item.Item.EnqueuedAt).TotalMilliseconds);
                }

                _metrics?.ActiveWorkers.Add(1);
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    await _processor.ProcessAsync(item, cancellationToken);
                    await _queue.AcknowledgeAsync(item, cancellationToken);
                    _metrics?.Succeeded.Add(1);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogDebug("Message worker {Consumer} cancelled while processing {EntryId}", consumer, item.EntryId);
                    _metrics?.Cancelled.Add(1);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Message worker {Consumer} failed for {EntryId}", consumer, item.EntryId);
                    _metrics?.Failed.Add(1);
                }
                finally
                {
                    stopwatch.Stop();
                    _metrics?.ProcessingDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
                    _metrics?.ActiveWorkers.Add(-1);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _readCancellation.IsCancellationRequested)
        {
        }
    }

    private async Task RunInteractionWorkerAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var interaction in _interactionChannel!.ReadAllAsync(cancellationToken))
            {
                _metrics?.Dequeued.Add(1);
                _metrics?.QueueDepth.Add(-1);
                _metrics?.ActiveWorkers.Add(1);
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    if (_interactionProcessor is not null)
                    {
                        await _interactionProcessor(interaction, cancellationToken);
                    }
                    else
                    {
                        await _interactionHandler!.ExecuteAsync(interaction, _services!);
                    }
                    _metrics?.Succeeded.Add(1);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogDebug("Interaction worker cancelled");
                    _metrics?.Cancelled.Add(1);
                    await SendInteractionFailureAsync(interaction);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Interaction worker failed for {InteractionId}", interaction.Id);
                    _metrics?.Failed.Add(1);
                    await SendInteractionFailureAsync(interaction);
                }
                finally
                {
                    stopwatch.Stop();
                    _metrics?.ProcessingDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
                    _metrics?.ActiveWorkers.Add(-1);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task SendInteractionFailureAsync(SocketInteraction interaction)
    {
        if (interaction is null)
        {
            _logger.LogError("Interaction processing failed before a follow-up could be sent");
            return;
        }

        try
        {
            await interaction.FollowupAsync("Failed to process this interaction.", ephemeral: true);
        }
        catch (Exception followupException)
        {
            _logger.LogError(followupException, "Failed to send interaction failure response for {InteractionId}", interaction.Id);
        }
    }
}
