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
    private readonly List<Task> _workers = [];

    public WorkQueueHostedService(
        IMessageWorkQueue queue,
        IWorkItemProcessor processor,
        WorkQueueOptions options,
        ILogger<WorkQueueHostedService> logger,
        InteractionWorkChannel? interactionChannel = null,
        InteractionHandler? interactionHandler = null,
        IServiceProvider? services = null,
        SaucyBotMetrics? metrics = null)
    {
        _queue = queue;
        _processor = processor;
        _options = options;
        _logger = logger;
        _interactionChannel = interactionChannel;
        _interactionHandler = interactionHandler;
        _services = services;
        _metrics = metrics;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var count = Math.Max(1, _options.MessageWorkerCount);
        for (var i = 0; i < count; i++)
        {
            _workers.Add(RunWorkerAsync($"{Environment.MachineName}-{i}", stoppingToken));
        }

        if (_interactionChannel is not null && _interactionHandler is not null && _services is not null)
        {
            for (var i = 0; i < Math.Max(1, _options.InteractionWorkerCount); i++)
            {
                _workers.Add(RunInteractionWorkerAsync(stoppingToken));
            }
        }

        await Task.WhenAll(_workers);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.ShutdownDrainTimeout);
        await base.StopAsync(timeout.Token);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task RunWorkerAsync(string consumer, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in _queue.ReadAsync(consumer, cancellationToken))
            {
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
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task RunInteractionWorkerAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var interaction in _interactionChannel!.ReadAllAsync(cancellationToken))
            {
                try
                {
                    await _interactionHandler!.ExecuteAsync(interaction, _services!);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogDebug("Interaction worker cancelled");
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Interaction worker failed for {InteractionId}", interaction.Id);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
