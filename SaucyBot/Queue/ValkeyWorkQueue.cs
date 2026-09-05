using SaucyBot.Diagnostics;
using StackExchange.Redis;

namespace SaucyBot.Queue;

public sealed class ValkeyBackpressureException(string message) : Exception(message);

public sealed record ValkeyStreamEntry(string EntryId, string Payload);

public interface IValkeyStreamClient
{
    Task EnsureGroupAsync(CancellationToken cancellationToken);
    Task<string> AddAsync(string payload, CancellationToken cancellationToken);
    Task<ValkeyStreamEntry?> ReadNewAsync(string consumer, CancellationToken cancellationToken);
    Task AcknowledgeAsync(string entryId, CancellationToken cancellationToken);
    Task DeleteAsync(string entryId, CancellationToken cancellationToken);
    Task ClearPendingAsync(CancellationToken cancellationToken);
}

public sealed class ValkeyWorkQueue : IMessageWorkQueue
{
    internal const string PayloadField = "payload";
    private readonly IValkeyStreamClient _client;
    private readonly WorkQueueOptions _options;
    private readonly SaucyBotMetrics? _metrics;
    private readonly ILogger<ValkeyWorkQueue> _logger;

    public ValkeyWorkQueue(
        IValkeyStreamClient client,
        WorkQueueOptions options,
        SaucyBotMetrics? metrics = null,
        ILogger<ValkeyWorkQueue>? logger = null)
    {
        _client = client;
        _options = options;
        _metrics = metrics;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ValkeyWorkQueue>.Instance;
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
                catch (ValkeyBackpressureException)
                {
                    _metrics?.Retried.Add(1);
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
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await _client.EnsureGroupAsync(cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            var entry = await _client.ReadNewAsync(consumer, cancellationToken);
            if (entry is not null)
            {
                MessageWorkItem? item = null;
                try
                {
                    item = MessageWorkItem.Deserialize(entry.Payload);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    _logger.LogError(exception, "Discarding malformed work item {EntryId}", entry.EntryId);
                    _metrics?.Malformed.Add(1);
                    await AcknowledgeAndDeleteMalformedAsync(entry.EntryId, cancellationToken);
                }

                if (item is not null)
                {
                    yield return new QueuedMessageWorkItem(entry.EntryId, item);
                }
            }
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

    public Task ClearPendingAsync(CancellationToken cancellationToken) =>
        _options.ClearPendingOnStartup ? _client.ClearPendingAsync(cancellationToken) : Task.CompletedTask;

    private async Task AcknowledgeAndDeleteMalformedAsync(string entryId, CancellationToken cancellationToken)
    {
        try
        {
            await _client.AcknowledgeAsync(entryId, cancellationToken);
            await _client.DeleteAsync(entryId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _metrics?.CleanupFailed.Add(1);
            _logger.LogError(exception, "Failed to discard malformed work item {EntryId}", entryId);
        }
    }
}

public sealed class StackExchangeValkeyStreamClient : IValkeyStreamClient
{
    private readonly IDatabase _database;
    private readonly WorkQueueOptions _options;

    public StackExchangeValkeyStreamClient(IConnectionMultiplexer connection, WorkQueueOptions options)
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
            var id = await _database.StreamAddAsync(_options.StreamName, ValkeyWorkQueue.PayloadField, payload)
                .WaitAsync(cancellationToken);
            return id.ToString();
        }
        catch (RedisServerException exception) when (exception.Message.Contains("MISCONF", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("OOM", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValkeyBackpressureException(exception.Message);
        }
        catch (RedisConnectionException exception)
        {
            throw new ValkeyBackpressureException(exception.Message);
        }
    }

    public async Task<ValkeyStreamEntry?> ReadNewAsync(string consumer, CancellationToken cancellationToken)
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

    private static ValkeyStreamEntry ToEntry(StreamEntry entry)
    {
        var payload = entry.Values.FirstOrDefault(x => x.Name == ValkeyWorkQueue.PayloadField).Value;
        return new ValkeyStreamEntry(entry.Id.ToString(), payload.ToString());
    }
}
