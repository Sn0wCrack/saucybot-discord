namespace SaucyBot.Queue.Redis;

internal static class RedisQueueScripts
{
    // All keys in this script are the configured stream key. Keeping scripts to
    // one key makes them valid on Redis Cluster without hash-tag requirements.
    internal const string RenewLease = """
        local pending = redis.call('XPENDING', KEYS[1], ARGV[1], ARGV[2], ARGV[2], 1)
        if #pending == 0 then return 0 end
        local expected = ARGV[3] .. '|' .. ARGV[4]
        if pending[1][2] ~= expected then return 0 end
        local claimed = redis.call('XCLAIM', KEYS[1], ARGV[1], expected, 0, ARGV[2], 'JUSTID')
        if #claimed == 0 then return 0 end
        return 1
        """;

    internal const string CompleteLease = """
        local pending = redis.call('XPENDING', KEYS[1], ARGV[1], ARGV[2], ARGV[2], 1)
        if #pending == 0 then
            local entry = redis.call('XRANGE', KEYS[1], ARGV[2], ARGV[2], 'COUNT', 1)
            if #entry == 0 then return 2 end
            redis.call('XDEL', KEYS[1], ARGV[2])
            return 2
        end
        local expected = ARGV[3] .. '|' .. ARGV[4]
        if pending[1][2] ~= expected then return -1 end
        if redis.call('XACK', KEYS[1], ARGV[1], ARGV[2]) == 0 then return -1 end
        redis.call('XDEL', KEYS[1], ARGV[2])
        return 1
        """;

    internal const string RetryLease = """
        local pending = redis.call('XPENDING', KEYS[1], ARGV[1], ARGV[2], ARGV[2], 1)
        if #pending == 0 then return 2 end
        local expected = ARGV[3] .. '|' .. ARGV[4]
        if pending[1][2] ~= expected then return -1 end
        return 1
        """;
}
