namespace SaucyBot.Queue.Redis;

// The enqueue command was issued, but Redis never confirmed whether it was
// applied. The caller must not retry automatically because Redis can still
// accept the item after the caller stops waiting.
public sealed class RedisEnqueueAmbiguousException : Exception
{
    public RedisEnqueueAmbiguousException(string message)
        : base(message)
    {
    }

    public RedisEnqueueAmbiguousException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
