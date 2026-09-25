using System;
using Microsoft.Extensions.Options;
using SaucyBot.Queue.Redis;
using Xunit;

namespace SaucyBot.Tests.Unit.Queue;

public sealed class RedisWorkQueueOptionsValidatorTest
{
    [Fact]
    public void Validate_WithDefaultOptions_DoesNotThrow()
    {
        RedisWorkQueueOptionsValidator.Validate(new RedisWorkQueueOptions());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_WithNonpositiveMalformedCleanupMaxAttempts_Throws(int attempts)
    {
        AssertRejected(new RedisWorkQueueOptions { MalformedCleanupMaxAttempts = attempts });
    }

    [Fact]
    public void Validate_WithNonpositiveRetryDelay_Throws()
    {
        AssertRejected(new RedisWorkQueueOptions { RetryDelay = TimeSpan.Zero });
        AssertRejected(new RedisWorkQueueOptions { RetryDelay = TimeSpan.FromSeconds(-1) });
    }

    [Fact]
    public void Validate_WithNonpositivePendingReadTimeout_Throws()
    {
        AssertRejected(new RedisWorkQueueOptions { PendingReadTimeout = TimeSpan.Zero });
        AssertRejected(new RedisWorkQueueOptions { PendingReadTimeout = TimeSpan.FromSeconds(-1) });
    }

    [Fact]
    public void Validate_WithNonpositiveMalformedCleanupMaxDelay_Throws()
    {
        AssertRejected(new RedisWorkQueueOptions { MalformedCleanupMaxDelay = TimeSpan.Zero });
        AssertRejected(new RedisWorkQueueOptions { MalformedCleanupMaxDelay = TimeSpan.FromSeconds(-1) });
    }

    private static void AssertRejected(RedisWorkQueueOptions options) =>
        Assert.Throws<OptionsValidationException>(() => RedisWorkQueueOptionsValidator.Validate(options));
}
