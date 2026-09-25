using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SaucyBot.Diagnostics;

namespace SaucyBot.Queue.Redis;

internal sealed class RedisWorkItemLease : IWorkItemLease
{
    private readonly IRedisStreamClient _client;
    private readonly WorkQueueOptions _options;
    private readonly string _consumer;
    private readonly string _entryId;
    private readonly string _leaseToken;
    private readonly Func<Exception, CancellationToken, Task<LeaseOperationResult>> _retryOperation;
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
        Func<Exception, CancellationToken, Task<LeaseOperationResult>> retryOperation,
        ISaucyBotMetrics? metrics = null,
        ILogger? logger = null)
    {
        _client = client;
        _options = options;
        _consumer = consumer;
        _entryId = entryId;
        _leaseToken = leaseToken;
        _retryOperation = retryOperation;
        _metrics = metrics;
        _logger = logger ?? NullLogger<RedisWorkItemLease>.Instance;
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

        var result = await _client.CompleteAsync(_consumer, _entryId, _leaseToken, cancellationToken);
        if (result == LeaseOperationResult.LeaseLost)
        {
            MarkLost();
        }

        return result;
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

        var result = await _retryOperation(exception, cancellationToken);
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
                var renewed = await _client.RenewAsync(
                    _consumer,
                    _entryId,
                    _leaseToken,
                    renewalLifetime.Token);
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
