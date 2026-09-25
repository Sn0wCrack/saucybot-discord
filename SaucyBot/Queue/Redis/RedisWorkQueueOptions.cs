namespace SaucyBot.Queue.Redis;

public sealed record RedisWorkQueueOptions
{
    public string ConnectionString { get; init; } = "queue:6379";
    public string StreamName { get; init; } = "saucybot:messages";
    public string ConsumerGroup { get; init; } = "saucybot-workers";
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan PendingReadTimeout { get; init; } = TimeSpan.FromSeconds(1);
    public int MalformedCleanupMaxAttempts { get; init; } = 3;
    public TimeSpan MalformedCleanupMaxDelay { get; init; } = TimeSpan.FromSeconds(5);
}
