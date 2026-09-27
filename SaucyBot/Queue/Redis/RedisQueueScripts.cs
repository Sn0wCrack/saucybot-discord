namespace SaucyBot.Queue.Redis;

internal static class RedisQueueScripts
{
    /// <summary>
    /// Renews a lease for a pending stream entry.
    /// </summary>
    /// <remarks>
    /// <para>This script uses one Redis key. This also allows Redis Cluster to route the script.</para>
    /// <para>Inputs:</para>
    /// <list type="bullet">
    /// <item><description><c>KEYS[1]</c> is the configured stream key.</description></item>
    /// <item><description><c>ARGV[1]</c> is the consumer group name.</description></item>
    /// <item><description><c>ARGV[2]</c> is the exact stream entry ID.</description></item>
    /// <item><description><c>ARGV[3]</c> is the base consumer name.</description></item>
    /// <item><description><c>ARGV[4]</c> is the lease token for this entry.</description></item>
    /// </list>
    /// <para>The owner name is <c>ARGV[3]|ARGV[4]</c>. Keep this format in sync with <c>RedisWorkQueue.ConsumerName</c>.</para>
    /// <para>Pass input through <c>KEYS</c> and <c>ARGV</c>. Do not add input values to the script source.</para>
    /// <para>Returns <c>1</c> when the pending entry belongs to this owner and <c>XCLAIM</c> succeeds.</para>
    /// <para>Returns <c>0</c> when the entry is not pending for this owner or <c>XCLAIM</c> returns no entry.</para>
    /// <para><c>XCLAIM</c> uses <c>JUSTID</c>. Redis resets the idle time without increasing the delivery count.</para>
    /// </remarks>
    internal const string RenewLease = """
        local pending = redis.call('XPENDING', KEYS[1], ARGV[1], ARGV[2], ARGV[2], 1)
        if #pending == 0 then return 0 end
        local expected = ARGV[3] .. '|' .. ARGV[4]
        if pending[1][2] ~= expected then return 0 end
        local claimed = redis.call('XCLAIM', KEYS[1], ARGV[1], expected, 0, ARGV[2], 'JUSTID')
        if #claimed == 0 then return 0 end
        return 1
        """;

    /// <summary>
    /// Acknowledges and removes a completed stream entry.
    /// </summary>
    /// <remarks>
    /// <para>This script uses one Redis key. This also allows Redis Cluster to route the script.</para>
    /// <para>Inputs:</para>
    /// <list type="bullet">
    /// <item><description><c>KEYS[1]</c> is the configured stream key.</description></item>
    /// <item><description><c>ARGV[1]</c> is the consumer group name.</description></item>
    /// <item><description><c>ARGV[2]</c> is the exact stream entry ID.</description></item>
    /// <item><description><c>ARGV[3]</c> is the base consumer name.</description></item>
    /// <item><description><c>ARGV[4]</c> is the lease token for this entry.</description></item>
    /// </list>
    /// <para>The owner name is <c>ARGV[3]|ARGV[4]</c>. Keep this format in sync with <c>RedisWorkQueue.ConsumerName</c>.</para>
    /// <para>Pass input through <c>KEYS</c> and <c>ARGV</c>. Do not add input values to the script source.</para>
    /// <para>Returns <c>1</c> when <c>XACK</c> acknowledges the entry. The script then calls <c>XDEL</c>.</para>
    /// <para>Returns <c>2</c> when the entry and its pending record are absent.</para>
    /// <para>Returns <c>-1</c> when this owner no longer has the pending entry or <c>XACK</c> acknowledges no entry.</para>
    /// <para>This queue uses one consumer group per stream. <c>XACK</c> affects one group, but <c>XDEL</c> removes the entry for all groups.</para>
    /// <para>The script removes the consumer record when that consumer has no pending entries left.</para>
    /// </remarks>
    internal const string CompleteLease = """
        local pending = redis.call('XPENDING', KEYS[1], ARGV[1], ARGV[2], ARGV[2], 1)
        local expected = ARGV[3] .. '|' .. ARGV[4]
        if #pending == 0 then
            local entry = redis.call('XRANGE', KEYS[1], ARGV[2], ARGV[2], 'COUNT', 1)
            if #entry > 0 then return -1 end
            local remaining = redis.call('XPENDING', KEYS[1], ARGV[1], '-', '+', 1, expected)
            if #remaining == 0 then
                redis.call('XGROUP', 'DELCONSUMER', KEYS[1], ARGV[1], expected)
            end
            return 2
        end
        if pending[1][2] ~= expected then return -1 end
        if redis.call('XACK', KEYS[1], ARGV[1], ARGV[2]) == 0 then return -1 end
        redis.call('XDEL', KEYS[1], ARGV[2])
        local remaining = redis.call('XPENDING', KEYS[1], ARGV[1], '-', '+', 1, expected)
        if #remaining == 0 then
            redis.call('XGROUP', 'DELCONSUMER', KEYS[1], ARGV[1], expected)
        end
        return 1
        """;

    /// <summary>
    /// Confirms that a failed entry still belongs to this lease.
    /// </summary>
    /// <remarks>
    /// <para>This script uses one Redis key. This also allows Redis Cluster to route the script.</para>
    /// <para>Inputs:</para>
    /// <list type="bullet">
    /// <item><description><c>KEYS[1]</c> is the configured stream key.</description></item>
    /// <item><description><c>ARGV[1]</c> is the consumer group name.</description></item>
    /// <item><description><c>ARGV[2]</c> is the exact stream entry ID.</description></item>
    /// <item><description><c>ARGV[3]</c> is the base consumer name.</description></item>
    /// <item><description><c>ARGV[4]</c> is the lease token for this entry.</description></item>
    /// </list>
    /// <para>The owner name is <c>ARGV[3]|ARGV[4]</c>. Keep this format in sync with <c>RedisWorkQueue.ConsumerName</c>.</para>
    /// <para>Pass input through <c>KEYS</c> and <c>ARGV</c>. Do not add input values to the script source.</para>
    /// <para>Returns <c>1</c> when this owner has the pending entry. Returns <c>-1</c> when it does not.</para>
    /// <para>This script does not change the pending entry. Recovery will reclaim it when it becomes idle.</para>
    /// </remarks>
    internal const string RetryLease = """
        local pending = redis.call('XPENDING', KEYS[1], ARGV[1], ARGV[2], ARGV[2], 1)
        if #pending == 0 then return -1 end
        local expected = ARGV[3] .. '|' .. ARGV[4]
        if pending[1][2] ~= expected then return -1 end
        return 1
        """;
}
