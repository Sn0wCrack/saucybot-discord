namespace SaucyBot.Queue;

public sealed class RedisBackpressureException(string message) : Exception(message);
