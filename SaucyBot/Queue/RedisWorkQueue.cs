using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using SaucyBot.Diagnostics;
using StackExchange.Redis;

namespace SaucyBot.Queue;

public sealed class RedisBackpressureException(string message) : Exception(message);

public sealed record RedisStreamEntry(string EntryId, string Payload);

public interface IRedisStreamClient
{
    Task EnsureGroupAsync(CancellationToken cancellationToken);
    Task<string> AddAsync(string payload, CancellationToken cancellationToken);
    Task<RedisStreamEntry?> ReadNewAsync(string consumer, CancellationToken cancellationToken);
    Task AcknowledgeAsync(string entryId, CancellationToken cancellationToken);
    Task DeleteAsync(string entryId, CancellationToken cancellationToken);
    Task ClearPendingAsync(CancellationToken cancellationToken);
}

public sealed class RedisWorkQueue : IMessageWorkQueue
{
    private enum MalformedCleanupOperation
    {
        Acknowledge,
        Delete,
    }

    internal const string PayloadField = "payload";
    private readonly IRedisStreamClient _client;
    private readonly WorkQueueOptions _options;
    private readonly ISaucyBotMetrics? _metrics;
    private readonly ILogger<RedisWorkQueue> _logger;

    public RedisWorkQueue(
        IRedisStreamClient client,
        WorkQueueOptions options,
        ISaucyBotMetrics? metrics = null,
        ILogger<RedisWorkQueue>? logger = null)
    {
        _client = client;
        _options = options;
        _metrics = metrics;
        _logger = logger ?? NullLogger<RedisWorkQueue>.Instance;
    }

    public async Task EnqueueAsync(MessageWorkItem item, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await _client.AddAsync(item.Serialize(), cancellationToken);
                    _metrics?.Enqueued.Add(1);
                    _metrics?.QueueDepth.Add(1);
                    return;
                }
                catch (RedisBackpressureException)
                {
                    _metrics?.Retried.Add(1);
                    _logger.LogDebug(
                        "Redis queue is applying backpressure; retrying enqueue after {RetryDelay}",
                        _options.RetryDelay);
                    await Task.Delay(_options.RetryDelay, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _metrics?.Cancelled.Add(1);
            throw;
        }
    }

    public async IAsyncEnumerable<QueuedMessageWorkItem> ReadAsync(
        string consumer,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await _client.EnsureGroupAsync(cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            var entry = await _client.ReadNewAsync(consumer, cancellationToken);
            if (entry is null)
            {
                continue;
            }

            MessageWorkItem item;
            try
            {
                item = MessageWorkItem.Deserialize(entry.Payload);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(exception, "Discarding malformed work item {EntryId}", entry.EntryId);
                _metrics?.Malformed.Add(1);
                _metrics?.QueueDepth.Add(-1);
                await DiscardMalformedAsync(entry.EntryId, cancellationToken);
                continue;
            }

            yield return new QueuedMessageWorkItem(entry.EntryId, item);
        }
    }

    public async Task AcknowledgeAsync(QueuedMessageWorkItem item, CancellationToken cancellationToken)
    {
        await _client.AcknowledgeAsync(item.EntryId, cancellationToken);
        try
        {
            await _client.DeleteAsync(item.EntryId, cancellationToken);
        }
        catch (Exception exception)
        {
            _metrics?.CleanupFailed.Add(1);
            _logger.LogError(exception, "Acknowledged work item {EntryId} but failed to delete it", item.EntryId);
            throw;
        }
    }

    public Task ClearPendingAsync(CancellationToken cancellationToken)
    {
        if (!_options.ClearPendingOnStartup)
        {
            return Task.CompletedTask;
        }

        return _client.ClearPendingAsync(cancellationToken);
    }

    private async Task DiscardMalformedAsync(string entryId, CancellationToken cancellationToken)
    {
        var acknowledged = await RetryMalformedCleanupAsync(
            entryId,
            MalformedCleanupOperation.Acknowledge,
            () => _client.AcknowledgeAsync(entryId, cancellationToken),
            cancellationToken);

        if (!acknowledged)
        {
            return;
        }

        await RetryMalformedCleanupAsync(
            entryId,
            MalformedCleanupOperation.Delete,
            () => _client.DeleteAsync(entryId, cancellationToken),
            cancellationToken);
    }

    private async Task<bool> RetryMalformedCleanupAsync(
        string entryId,
        MalformedCleanupOperation operation,
        Func<Task> cleanup,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                await cleanup();
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug("Cancelled malformed work item cleanup {Operation} for {EntryId}", operation, entryId);
                return false;
            }
            catch (Exception exception)
            {
                _metrics?.CleanupFailed.Add(1);
                _logger.LogWarning(exception, "Retrying malformed work item cleanup {Operation} for {EntryId}", operation, entryId);
                await Task.Delay(_options.RetryDelay, cancellationToken);
            }
        }
    }
}

public sealed class StackExchangeRedisStreamClient : IRedisStreamClient
{
    private readonly IDatabase _database;
    private readonly WorkQueueOptions _options;

    public StackExchangeRedisStreamClient(IConnectionMultiplexer connection, WorkQueueOptions options)
    {
        _database = connection.GetDatabase();
        _options = options;
    }

    public async Task EnsureGroupAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _database.StreamCreateConsumerGroupAsync(_options.StreamName, _options.ConsumerGroup, "0-0", createStream: true)
                .WaitAsync(cancellationToken);
        }
        catch (RedisServerException exception) when (exception.Message.Contains("BUSYGROUP", StringComparison.OrdinalIgnoreCase))
        {
        }
    }

    public async Task<string> AddAsync(string payload, CancellationToken cancellationToken)
    {
        try
        {
            var id = await _database.StreamAddAsync(_options.StreamName, RedisWorkQueue.PayloadField, payload)
                .WaitAsync(cancellationToken);
            return id.ToString();
        }
        catch (RedisServerException exception) when (exception.Message.Contains("MISCONF", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("OOM", StringComparison.OrdinalIgnoreCase))
        {
            throw new RedisBackpressureException(exception.Message);
        }
        catch (RedisConnectionException exception)
        {
            throw new RedisBackpressureException(exception.Message);
        }
    }

    public async Task<RedisStreamEntry?> ReadNewAsync(string consumer, CancellationToken cancellationToken)
    {
        var entries = await _database.StreamReadGroupAsync(_options.StreamName, _options.ConsumerGroup, consumer, ">", count: 1)
            .WaitAsync(cancellationToken);

        if (entries.Length == 0)
        {
            await Task.Delay(_options.RetryDelay, cancellationToken);
            return null;
        }

        return ToEntry(entries[0]);
    }

    public Task AcknowledgeAsync(string entryId, CancellationToken cancellationToken) =>
        _database.StreamAcknowledgeAsync(_options.StreamName, _options.ConsumerGroup, entryId)
            .WaitAsync(cancellationToken);

    public Task DeleteAsync(string entryId, CancellationToken cancellationToken) =>
        _database.StreamDeleteAsync(_options.StreamName, [entryId]).WaitAsync(cancellationToken);

    public Task ClearPendingAsync(CancellationToken cancellationToken) =>
        _database.KeyDeleteAsync(_options.StreamName).WaitAsync(cancellationToken);

    private static RedisStreamEntry ToEntry(StreamEntry entry)
    {
        var payload = entry.Values.FirstOrDefault(x => x.Name == RedisWorkQueue.PayloadField).Value;
        return new RedisStreamEntry(entry.Id.ToString(), payload.ToString());
    }
}
