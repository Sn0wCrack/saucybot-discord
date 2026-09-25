using System;
using Microsoft.Extensions.Options;
using SaucyBot.Queue;
using Xunit;

namespace SaucyBot.Tests.Unit.Queue;

public sealed class WorkQueueOptionsValidatorTest
{
    [Fact]
    public void Validate_WithDefaultOptions_DoesNotThrow()
    {
        WorkQueueOptionsValidator.Validate(new WorkQueueOptions());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_WithNonpositiveMessageWorkerCount_Throws(int count)
    {
        AssertRejected(new WorkQueueOptions { MessageWorkerCount = count });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_WithNonpositiveInteractionWorkerCount_Throws(int count)
    {
        AssertRejected(new WorkQueueOptions { InteractionWorkerCount = count });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_WithNonpositiveRetryCount_Throws(int count)
    {
        AssertRejected(new WorkQueueOptions { MaxProcessingAttempts = count });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_WithNonpositiveInteractionChannelCapacity_Throws(int capacity)
    {
        AssertRejected(new WorkQueueOptions { InteractionChannelCapacity = capacity });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    public void Validate_WithInsufficientRecoveryHandoffCapacity_Throws(int capacity)
    {
        AssertRejected(new WorkQueueOptions { RecoveryHandoffCapacity = capacity });
    }

    [Fact]
    public void Validate_WithNonpositiveEnqueueTimeout_Throws()
    {
        AssertRejected(new WorkQueueOptions { EnqueueTimeout = TimeSpan.Zero });
        AssertRejected(new WorkQueueOptions { EnqueueTimeout = TimeSpan.FromSeconds(-1) });
    }

    [Fact]
    public void Validate_WithNonpositiveBackendOperationTimeout_Throws()
    {
        AssertRejected(new WorkQueueOptions { BackendOperationTimeout = TimeSpan.Zero });
        AssertRejected(new WorkQueueOptions { BackendOperationTimeout = TimeSpan.FromSeconds(-1) });
    }

    [Fact]
    public void Validate_WithNonpositiveMaxProcessingTime_Throws()
    {
        AssertRejected(new WorkQueueOptions { MaxProcessingTime = TimeSpan.Zero });
    }

    [Fact]
    public void Validate_WithNonpositiveShutdownDrainTimeout_Throws()
    {
        AssertRejected(new WorkQueueOptions { ShutdownDrainTimeout = TimeSpan.Zero });
    }

    [Fact]
    public void Validate_WithNonpositiveHeartbeatInterval_Throws()
    {
        AssertRejected(new WorkQueueOptions { HeartbeatInterval = TimeSpan.Zero });
    }

    [Fact]
    public void Validate_WithNonpositivePendingMessageIdleTime_Throws()
    {
        AssertRejected(new WorkQueueOptions { PendingMessageIdleTime = TimeSpan.Zero });
    }

    [Fact]
    public void Validate_WithNonpositiveReclaimerInterval_Throws()
    {
        AssertRejected(new WorkQueueOptions { ReclaimerInterval = TimeSpan.Zero });
    }

    [Fact]
    public void Validate_WhenHeartbeatEqualsPendingMessageIdleTime_Throws()
    {
        AssertRejected(new WorkQueueOptions
        {
            HeartbeatInterval = TimeSpan.FromSeconds(30),
            PendingMessageIdleTime = TimeSpan.FromSeconds(30),
        });
    }

    [Fact]
    public void Validate_WhenHeartbeatExceedsPendingMessageIdleTime_Throws()
    {
        AssertRejected(new WorkQueueOptions
        {
            HeartbeatInterval = TimeSpan.FromSeconds(31),
            PendingMessageIdleTime = TimeSpan.FromSeconds(30),
        });
    }

    [Fact]
    public void Validate_WhenHeartbeatIsShorterThanPendingMessageIdleTime_DoesNotThrow()
    {
        WorkQueueOptionsValidator.Validate(new WorkQueueOptions
        {
            HeartbeatInterval = TimeSpan.FromSeconds(5),
            PendingMessageIdleTime = TimeSpan.FromSeconds(30),
        });
    }

    private static void AssertRejected(WorkQueueOptions options) =>
        Assert.Throws<OptionsValidationException>(() => WorkQueueOptionsValidator.Validate(options));
}
