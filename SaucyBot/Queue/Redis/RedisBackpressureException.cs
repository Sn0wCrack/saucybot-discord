namespace SaucyBot.Queue.Redis;

public sealed class RedisBackpressureException(string message) : Exception(message);
