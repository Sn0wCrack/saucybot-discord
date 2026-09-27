using System;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using SaucyBot.Queue;

namespace SaucyBot.Tests.Unit.Common;

internal static class TestData
{
    public static readonly Guid CorrelationId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static MessageWorkItem Message() => new(
        1,
        2,
        3,
        4,
        [5],
        "message",
        null,
        [],
        true,
        true,
        CorrelationId);

    public static IWorkItemLease NoOpLease()
    {
        var lease = Substitute.For<IWorkItemLease>();
        lease.LostToken.Returns(CancellationToken.None);
        lease.CompleteAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(LeaseOperationResult.Applied));
        lease.RetryAsync(Arg.Any<Exception>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(LeaseOperationResult.Applied));
        lease.DisposeAsync().Returns(ValueTask.CompletedTask);
        return lease;
    }

}
