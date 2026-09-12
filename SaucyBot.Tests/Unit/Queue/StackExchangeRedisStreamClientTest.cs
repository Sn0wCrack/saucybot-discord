using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SaucyBot.Queue;
using StackExchange.Redis;
using Xunit;

namespace SaucyBot.Tests.Unit.Queue;

public sealed class StackExchangeRedisStreamClientTest
{
    private const string Consumer = "worker-1";

    private static WorkQueueOptions Options(TimeSpan pendingReadTimeout) =>
        new()
        {
            ConnectionString = "queue:6379",
            StreamName = "saucybot:messages",
            ConsumerGroup = "testers",
            RetryDelay = TimeSpan.FromMilliseconds(10),
            PendingReadTimeout = pendingReadTimeout,
        };

    private readonly IConnectionMultiplexer _connection = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _database;

    public StackExchangeRedisStreamClientTest()
    {
        _database = Substitute.For<IDatabase>();
        _connection.GetDatabase().Returns(_database);
    }

    private StackExchangeRedisStreamClient CreateClient(WorkQueueOptions options) =>
        new(_connection, options, NullLogger<StackExchangeRedisStreamClient>.Instance);

    private void ConfigureRead(Task<StreamEntry[]> readTask) =>
        _database
            .StreamReadGroupAsync(
                Arg.Any<RedisKey>(),
                Arg.Any<RedisValue>(),
                Arg.Any<RedisValue>(),
                Arg.Any<RedisValue>(),
                Arg.Any<int?>(),
                Arg.Any<bool>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CommandFlags>())
            .Returns(readTask);

    private static StreamEntry SingleEntry() =>
        new("1-0", [new NameValueEntry(RedisWorkQueue.PayloadField, "hello")]);

    [Fact]
    public async Task ReadNewAsync_WhenInFlightReadNeverCompletes_ReturnsNullWithoutAcknowledging()
    {
        var incompleteRead = new TaskCompletionSource<StreamEntry[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        ConfigureRead(incompleteRead.Task);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var client = CreateClient(Options(TimeSpan.FromMilliseconds(50)));

        var sw = Stopwatch.StartNew();
        var readTask = client.ReadNewAsync(Consumer, cts.Token);
        await Task.Delay(200, TestContext.Current.CancellationToken);
        cts.Cancel();

        var result = await readTask;
        sw.Stop();

        Assert.Null(result);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"drain was not bounded, took {sw.Elapsed}");
        await _database.DidNotReceiveWithAnyArgs().StreamAcknowledgeAsync(
            (RedisKey)default,
            (RedisValue)default,
            (RedisValue)default,
            default);
    }

    [Fact]
    public async Task ReadNewAsync_WhenInFlightReadCompletesWithinGrace_AcknowledgesAndRethrowsCancellation()
    {
        var pendingRead = new TaskCompletionSource<StreamEntry[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        ConfigureRead(pendingRead.Task);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var client = CreateClient(Options(TimeSpan.FromSeconds(10)));

        var readTask = client.ReadNewAsync(Consumer, cts.Token);
        await Task.Delay(200, TestContext.Current.CancellationToken);
        cts.Cancel();
        await Task.Delay(300, TestContext.Current.CancellationToken);
        pendingRead.SetResult([SingleEntry()]);

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => readTask);

        Assert.Equal(cts.Token, exception.CancellationToken);
        await _database.Received(1).StreamAcknowledgeAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<RedisValue>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task ReadNewAsync_WhenInFlightReadFaultsDuringDrain_ReturnsNullWithoutAcknowledging()
    {
        var faultingRead = new TaskCompletionSource<StreamEntry[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        ConfigureRead(faultingRead.Task);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var client = CreateClient(Options(TimeSpan.FromSeconds(10)));

        var readTask = client.ReadNewAsync(Consumer, cts.Token);
        await Task.Delay(200, TestContext.Current.CancellationToken);
        cts.Cancel();
        await Task.Delay(300, TestContext.Current.CancellationToken);
        faultingRead.SetException(new RedisTimeoutException(CommandFlags.None, "simulated stall", CommandStatus.WaitingToBeSent));

        var result = await readTask;

        Assert.Null(result);
        await _database.DidNotReceiveWithAnyArgs().StreamAcknowledgeAsync(
            (RedisKey)default,
            (RedisValue)default,
            (RedisValue)default,
            default);
    }
}
