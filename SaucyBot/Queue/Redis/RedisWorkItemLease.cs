using Microsoft.Extensions.Logging;
using SaucyBot.Diagnostics;

namespace SaucyBot.Queue.Redis;

internal sealed class RedisWorkItemLease : IWorkItemLease
{
    private readonly IRedisStreamClient _client;
    private readonly WorkQueueOptions _options;
    private readonly string _consumer;
    private readonly string _entryId;
    private readonly string _leaseToken;
    private readonly int _attempt;
    private readonly ISaucyBotMetrics? _metrics;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _renewalStop = new();
    private readonly CancellationTokenSource _lost = new();
    private readonly CancellationToken _lostToken;
    private readonly Task _renewal;
    private int _renewalStopped;
    private int _disposed;

    public RedisWorkItemLease(
        IRedisStreamClient client,
        WorkQueueOptions options,
        string consumer,
        string entryId,
        string leaseToken,
        int attempt,
        ILogger logger,
        ISaucyBotMetrics? metrics = null)
    {
        _client = client;
        _options = options;
        _consumer = consumer;
        _entryId = entryId;
        _leaseToken = leaseToken;
        _attempt = attempt;
        _metrics = metrics;
        _logger = logger;
        _lostToken = _lost.Token;
        _renewal = RenewAsync();
    }

    public CancellationToken LostToken => _lostToken;

    public async Task<LeaseOperationResult> CompleteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await StopRenewalAsync();
        if (_lostToken.IsCancellationRequested)
        {
            return LeaseOperationResult.LeaseLost;
        }

        return await ExecuteMutationAsync(
            "complete",
            token => _client.CompleteAsync(_consumer, _entryId, _leaseToken, token),
            cancellationToken);
    }

    public async Task<LeaseOperationResult> RetryAsync(Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exception);
        cancellationToken.ThrowIfCancellationRequested();
        await StopRenewalAsync();
        if (_lostToken.IsCancellationRequested)
        {
            return LeaseOperationResult.LeaseLost;
        }

        return await ExecuteMutationAsync(
            "retry",
            token => ApplyRetryPolicyAsync(exception, token),
            cancellationToken);
    }

    private async Task<LeaseOperationResult> ApplyRetryPolicyAsync(
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (_attempt < Math.Max(1, _options.MaxProcessingAttempts))
        {
            _logger.LogWarning(
                exception,
                "Queue entry {EntryId} failed; Redis will retry it after it becomes idle (attempt {Attempt} of {MaxAttempts})",
                _entryId,
                _attempt,
                Math.Max(1, _options.MaxProcessingAttempts));
            return await _client.RetryAsync(_consumer, _entryId, _leaseToken, cancellationToken);
        }

        _logger.LogError(
            exception,
            "Discarded queue entry {EntryId} after {Attempt} processing attempts",
            _entryId,
            _attempt);
        return await _client.CompleteAsync(_consumer, _entryId, _leaseToken, cancellationToken);
    }

    // Completion and retry scripts are fenced and idempotent for one lease
    // token, so an ambiguous outcome is retried once with the same token. When
    // the result stays unknown the item is left for recovery and the handler
    // must not be rerun here. A started mutation is always awaited to a bounded
    // end, even when the caller cancels, because its outcome decides whether
    // work stays pending.
    private async Task<LeaseOperationResult> ExecuteMutationAsync(
        string operationName,
        Func<CancellationToken, Task<LeaseOperationResult>> operation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = LeaseOperationResult.OutcomeUnknown;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                result = await operation(cancellationToken).WaitAsync(_options.BackendOperationTimeout);
            }
            catch (TimeoutException exception)
            {
                _metrics?.BackendOperationTimedOut.Add(
                    1,
                    QueueMetricTags.BackendOperation(operationName));
                _logger.LogWarning(
                    exception,
                    "Redis lease mutation for queue entry {EntryId} exceeded {BackendOperationTimeout}; the outcome is unknown",
                    _entryId,
                    _options.BackendOperationTimeout);
                result = LeaseOperationResult.OutcomeUnknown;
            }

            if (result != LeaseOperationResult.OutcomeUnknown || attempt >= 2)
            {
                break;
            }

            _logger.LogWarning(
                "Repeating the lease mutation for queue entry {EntryId} with the same lease token after an unknown outcome",
                _entryId);
        }

        if (result == LeaseOperationResult.LeaseLost)
        {
            MarkLost();
        }

        return result;
    }

    public async ValueTask DisposeAsync()
    {
        await StopRenewalAsync();
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _renewalStop.Dispose();
            _lost.Dispose();
        }
    }

    private async Task RenewAsync()
    {
        using var timer = new PeriodicTimer(_options.HeartbeatInterval);
        using var deadline = new CancellationTokenSource(_options.MaxProcessingTime);
        using var renewalLifetime = CancellationTokenSource.CreateLinkedTokenSource(
            _renewalStop.Token,
            deadline.Token);

        try
        {
            while (await timer.WaitForNextTickAsync(renewalLifetime.Token))
            {
                // Each renewal is bounded and cancellation-aware so one stalled
                // command cannot keep the lease busy past its operation timeout.
                var renewed = await _client.RenewAsync(
                        _consumer,
                        _entryId,
                        _leaseToken,
                        renewalLifetime.Token)
                    .WaitAsync(_options.BackendOperationTimeout, renewalLifetime.Token);
                if (!renewed)
                {
                    MarkLost();
                    return;
                }

                _metrics?.LeaseRenewed.Add(1, QueueMetricTags.Lease(_consumer));
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            MarkLost();
        }
        catch (TimeoutException exception)
        {
            _metrics?.BackendOperationTimedOut.Add(
                1,
                QueueMetricTags.BackendOperation("renew"));
            _logger.LogWarning(
                exception,
                "Redis lease renewal for queue entry {EntryId} exceeded {BackendOperationTimeout}",
                _entryId,
                _options.BackendOperationTimeout);
            MarkLost();
        }
        catch (OperationCanceledException) when (_renewalStop.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to renew the lease for queue entry {EntryId}; the delivery is considered lost",
                _entryId);
            MarkLost();
        }
    }

    private void MarkLost()
    {
        if (!_lostToken.IsCancellationRequested)
        {
            try
            {
                _lost.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // A disposed lease can still observe a fenced Redis result.
            }
        }
    }

    private async Task StopRenewalAsync()
    {
        if (Interlocked.Exchange(ref _renewalStopped, 1) == 0)
        {
            _renewalStop.Cancel();
        }

        await _renewal;
    }
}
