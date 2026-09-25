using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SaucyBot.Queue;
using SaucyBot.Queue.Redis;
using StackExchange.Redis;
using Xunit;

namespace SaucyBot.Tests.Unit.Queue;

public sealed class StackExchangeRedisStreamClientTest
{
    private const string Consumer = "worker-1";

    private static RedisWorkQueueOptions Options(TimeSpan pendingReadTimeout) =>
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
        _database
            .StreamAutoClaimAsync(
                Arg.Any<RedisKey>(),
                Arg.Any<RedisValue>(),
                Arg.Any<RedisValue>(),
                Arg.Any<long>(),
                Arg.Any<RedisValue>(),
                Arg.Any<int?>(),
                Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(StreamAutoClaimResult.Null));
    }

    private StackExchangeRedisStreamClient CreateClient(RedisWorkQueueOptions options) =>
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
        var readTask = client.ReadNewAsync(Consumer, "opaque-token", cts.Token);
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
    public async Task ReadNewAsync_WhenInFlightReadCompletesWithinGrace_LeavesEntryPendingAndRethrowsCancellation()
    {
        var pendingRead = new TaskCompletionSource<StreamEntry[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        ConfigureRead(pendingRead.Task);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var client = CreateClient(Options(TimeSpan.FromSeconds(10)));

        var readTask = client.ReadNewAsync(Consumer, "opaque-token", cts.Token);
        await Task.Delay(200, TestContext.Current.CancellationToken);
        cts.Cancel();
        await Task.Delay(300, TestContext.Current.CancellationToken);
        pendingRead.SetResult([SingleEntry()]);

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => readTask);

        Assert.Equal(cts.Token, exception.CancellationToken);
        await _database.DidNotReceiveWithAnyArgs().StreamAcknowledgeAsync(
            (RedisKey)default,
            (RedisValue)default,
            (RedisValue)default,
            default);
    }

    [Fact]
    public async Task ReadNewAsync_WhenInFlightReadFaultsDuringDrain_ReturnsNullWithoutAcknowledging()
    {
        var faultingRead = new TaskCompletionSource<StreamEntry[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        ConfigureRead(faultingRead.Task);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var client = CreateClient(Options(TimeSpan.FromSeconds(10)));

        var readTask = client.ReadNewAsync(Consumer, "opaque-token", cts.Token);
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

    [Theory]
    [InlineData(1, LeaseOperationResult.Applied)]
    [InlineData(2, LeaseOperationResult.AlreadyApplied)]
    [InlineData(-1, LeaseOperationResult.LeaseLost)]
    public async Task CompleteAsync_UsesOneStreamKeyAndMapsAtomicScriptResult(
        int scriptResult,
        LeaseOperationResult expected)
    {
        _database
            .ScriptEvaluateAsync(
                Arg.Any<string>(),
                Arg.Any<RedisKey[]>(),
                Arg.Any<RedisValue[]>(),
                Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(RedisResult.Create((RedisValue)scriptResult)));
        var client = CreateClient(Options(TimeSpan.FromSeconds(1)));

        var result = await client.CompleteAsync(
            Consumer,
            "42-0",
            "lease-token-opaque",
            CancellationToken.None);

        Assert.Equal(expected, result);
        var call = Assert.Single(_database.ReceivedCalls(), call => call.GetMethodInfo().Name == "ScriptEvaluateAsync");
        var keys = Assert.IsType<RedisKey[]>(call.GetArguments()[1]);
        Assert.Equal(["saucybot:messages"], keys.Select(key => key.ToString()));
        var values = Assert.IsType<RedisValue[]>(call.GetArguments()[2]);
        Assert.Contains((RedisValue)"lease-token-opaque", values);
    }

    [Fact]
    public async Task CompleteAsync_WhenRedisTimesOutReturnsOutcomeUnknown()
    {
        _database
            .ScriptEvaluateAsync(
                Arg.Any<string>(),
                Arg.Any<RedisKey[]>(),
                Arg.Any<RedisValue[]>(),
                Arg.Any<CommandFlags>())
            .Returns(Task.FromException<RedisResult>(
                new RedisTimeoutException(CommandFlags.None, "simulated completion timeout", CommandStatus.WaitingToBeSent)));
        var client = CreateClient(Options(TimeSpan.FromSeconds(1)));

        var result = await client.CompleteAsync(
            Consumer,
            "42-0",
            "lease-token-opaque",
            CancellationToken.None);

        Assert.Equal(LeaseOperationResult.OutcomeUnknown, result);
    }

    [Fact]
    public async Task AddAsync_WhenRedisTimesOutReportsAnAmbiguousEnqueueInsteadOfBackpressure()
    {
        _database
            .StreamAddAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<RedisValue>())
            .Returns(Task.FromException<RedisValue>(
                new RedisTimeoutException(CommandFlags.None, "simulated enqueue timeout", CommandStatus.WaitingToBeSent)));
        var client = CreateClient(Options(TimeSpan.FromSeconds(1)));

        await Assert.ThrowsAsync<RedisEnqueueAmbiguousException>(
            () => client.AddAsync("payload", CancellationToken.None));
    }

    [Fact]
    public async Task AddAsync_WhenConnectionFailsReportsAnAmbiguousEnqueueInsteadOfBackpressure()
    {
        _database
            .StreamAddAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<RedisValue>())
            .Returns(Task.FromException<RedisValue>(
                new RedisConnectionException(
                    ConnectionFailureType.UnableToConnect,
                    CommandFlags.None,
                    "simulated connection failure",
                    null,
                    CommandStatus.WaitingToBeSent)));
        var client = CreateClient(Options(TimeSpan.FromSeconds(1)));

        await Assert.ThrowsAsync<RedisEnqueueAmbiguousException>(
            () => client.AddAsync("payload", CancellationToken.None));
    }

    [Fact]
    public async Task AddAsync_WhenServerRejectsTheWriteMapsToRetryableBackpressure()
    {
        _database
            .StreamAddAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<RedisValue>())
            .Returns(Task.FromException<RedisValue>(
                new RedisServerException(
                    RedisErrorKind.Misconfigured,
                    CommandFlags.None,
                    "MISCONF Redis is configured to save RDB snapshots")));
        var client = CreateClient(Options(TimeSpan.FromSeconds(1)));

        await Assert.ThrowsAsync<RedisBackpressureException>(
            () => client.AddAsync("payload", CancellationToken.None));
    }

    [Fact]
    public void BuildConfiguration_SetsTheNativeCommandTimeout()
    {
        var configuration = ServiceRegistration.BuildConfiguration(
            Options(TimeSpan.FromSeconds(1)),
            TimeSpan.FromSeconds(3));

        Assert.Equal(3000, configuration.SyncTimeout);
        Assert.Equal(3000, configuration.AsyncTimeout);

        var connectionStringOverrides = Options(TimeSpan.FromSeconds(1)) with
        {
            ConnectionString = "queue:6379,syncTimeout=30000,asyncTimeout=30000",
        };
        var overridden = ServiceRegistration.BuildConfiguration(
            connectionStringOverrides,
            TimeSpan.FromSeconds(3));

        Assert.Equal(3000, overridden.SyncTimeout);
        Assert.Equal(3000, overridden.AsyncTimeout);
    }
}
