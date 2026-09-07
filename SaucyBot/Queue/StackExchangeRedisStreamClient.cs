using StackExchange.Redis;

namespace SaucyBot.Queue;

public sealed class StackExchangeRedisStreamClient(
    IConnectionMultiplexer connection,
    WorkQueueOptions options) : IRedisStreamClient
{
    private readonly IDatabase _database = connection.GetDatabase();
    private readonly WorkQueueOptions _options = options;

    public async Task EnsureGroupAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // WaitAsync cancels this caller's wait; it cannot cancel an issued Redis command.
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
            cancellationToken.ThrowIfCancellationRequested();
            // Do not issue a command after admission cancellation; WaitAsync is not command cancellation.
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
        cancellationToken.ThrowIfCancellationRequested();
        var read = _database.StreamReadGroupAsync(_options.StreamName, _options.ConsumerGroup, consumer, ">", count: 1);
        StreamEntry[] entries;
        try
        {
            entries = await read.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            entries = await read;
            foreach (var entry in entries)
            {
                await _database.StreamAcknowledgeAsync(_options.StreamName, _options.ConsumerGroup, entry.Id);
            }

            throw;
        }

        if (entries.Length == 0)
        {
            await Task.Delay(_options.RetryDelay, cancellationToken);
            return null;
        }

        return ToEntry(entries[0]);
    }

    public async Task AcknowledgeAsync(string entryId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Acknowledgement is issued only for completed work. Once issued, await the mutation to reconcile its result.
        await _database.StreamAcknowledgeAsync(_options.StreamName, _options.ConsumerGroup, entryId);
    }

    public async Task DeleteAsync(string entryId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Deletion follows XACK; once issued, await the idempotent mutation to reconcile its result.
        await _database.StreamDeleteAsync(_options.StreamName, [entryId]);
    }

    public Task ClearPendingAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _database.KeyDeleteAsync(_options.StreamName).WaitAsync(cancellationToken);
    }

    private static RedisStreamEntry ToEntry(StreamEntry entry)
    {
        var payload = entry.Values.FirstOrDefault(x => x.Name == RedisWorkQueue.PayloadField).Value;
        return new RedisStreamEntry(entry.Id.ToString(), payload.ToString());
    }
}
