using System.Diagnostics;
using SaucyBot.Diagnostics;

namespace SaucyBot.Queue;

public sealed class WorkQueueHostedService : BackgroundService, IAsyncDisposable
{
    private readonly IWorkItemConsumer<MessageWorkItem> _consumer;
    private readonly MessageDeliveryChannel _deliveryChannel;
    private readonly MessageQueueReader _messageReader;
    private readonly MessageRecoveryWorker _recoveryWorker;
    private readonly MessageQueueWorker _messageWorker;
    private readonly WorkQueueOptions _options;
    private readonly ILogger<WorkQueueHostedService> _logger;
    private readonly InteractionWorkChannel _interactionChannel;
    private readonly InteractionQueueWorker _interactionWorker;
    private readonly ISaucyBotMetrics _metrics;
    private readonly CancellationTokenSource _admissionCancellation = new();
    private readonly CancellationTokenSource _workerCancellation = new();
    private readonly CancellationTokenSource _readCancellation = new();
    private readonly TaskCompletionSource _completionReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _consumerInstance = $"{Environment.MachineName}-{Guid.NewGuid():N}";
    private Task? _completion;
    private int _disposed;

    public CancellationToken AdmissionToken => _admissionCancellation.Token;
    public Task WorkerCompletion => _completion ?? Task.CompletedTask;

    public WorkQueueHostedService(
        IWorkItemConsumer<MessageWorkItem> consumer,
        MessageDeliveryChannel deliveryChannel,
        MessageQueueReader messageReader,
        MessageRecoveryWorker recoveryWorker,
        MessageQueueWorker messageWorker,
        WorkQueueOptions options,
        ILogger<WorkQueueHostedService> logger,
        InteractionWorkChannel interactionChannel,
        InteractionQueueWorker interactionWorker,
        ISaucyBotMetrics metrics)
    {
        _consumer = consumer;
        _deliveryChannel = deliveryChannel;
        _messageReader = messageReader;
        _recoveryWorker = recoveryWorker;
        _messageWorker = messageWorker;
        _options = options;
        _logger = logger;
        _interactionChannel = interactionChannel;
        _interactionWorker = interactionWorker;
        _metrics = metrics;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var stoppingRegistration = stoppingToken.Register(StopIntake);
        var reader = RunReaderSupervisedAsync($"{_consumerInstance}-reader", _readCancellation.Token);
        var recovery = _recoveryWorker.RunAsync($"{_consumerInstance}-recovery", _readCancellation.Token);
        var messageWorkers = Math.Max(1, _options.MessageWorkerCount);
        var interactionWorkers = Math.Max(1, _options.InteractionWorkerCount);
        var consumers = new List<Task>(messageWorkers + interactionWorkers);

        for (var i = 0; i < messageWorkers; i++)
        {
            consumers.Add(_messageWorker.RunSupervisedAsync(
                $"{_consumerInstance}-message-{i}",
                _workerCancellation.Token));
        }

        for (var i = 0; i < interactionWorkers; i++)
        {
            consumers.Add(_interactionWorker.RunSupervisedAsync(_workerCancellation.Token));
        }

        _logger.LogInformation(
            "Queue workers started with {MessageWorkerCount} message workers and {InteractionWorkerCount} interaction workers",
            messageWorkers,
            interactionWorkers);

        _completion = CompleteAfterProducersAsync(Task.WhenAll(reader, recovery), Task.WhenAll(consumers));
        _ = _completion.ContinueWith(
            completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        _completionReady.TrySetResult();
        await _completion;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await _consumer.StartAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
        await _completionReady.Task.WaitAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping queue workers and draining admitted work");
        StopIntake();
        if (cancellationToken.IsCancellationRequested)
        {
            _workerCancellation.Cancel();
            await _deliveryChannel.DisposeAsync();
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
            await _deliveryChannel.DisposeAsync();
            throw;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Queue worker drain exceeded {ShutdownDrainTimeout}; cancelling remaining work",
                _options.ShutdownDrainTimeout);
            _workerCancellation.Cancel();
            await _deliveryChannel.DisposeAsync();
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
                _ => _ = DisposeResourcesAsync(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return ValueTask.CompletedTask;
        }

        return new ValueTask(DisposeResourcesAsync());
    }

    private async Task DisposeResourcesAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await _deliveryChannel.DisposeAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to dispose queued message deliveries");
        }
        finally
        {
            Dispose();
            _admissionCancellation.Dispose();
            _workerCancellation.Dispose();
            _readCancellation.Dispose();
        }
    }

    private async Task CompleteAfterProducersAsync(Task producers, Task consumers)
    {
        try
        {
            await producers;
        }
        finally
        {
            _deliveryChannel.Complete();
        }

        await consumers;
    }

    private async Task RunReaderSupervisedAsync(string consumer, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _messageReader.RunAsync(consumer, cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                _logger.LogWarning("Message queue reader {Consumer} stopped unexpectedly; restarting", consumer);
                _metrics.WorkerRestarts.Add(1, new KeyValuePair<string, object?>("worker_type", "reader"));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Message queue reader {Consumer} failed; restarting", consumer);
                _metrics.WorkerRestarts.Add(1, new KeyValuePair<string, object?>("worker_type", "reader"));
            }

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
