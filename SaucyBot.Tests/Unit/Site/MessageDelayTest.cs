using System;
using System.Threading;
using System.Threading.Tasks;
using SaucyBot.Site;

namespace SaucyBot.Tests.Unit.Site;

public sealed class ControlledMessageDelay : IMessageDelay
{
    public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        Started.SetResult(true);
        return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }
}
