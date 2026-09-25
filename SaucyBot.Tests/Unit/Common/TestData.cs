using System;
using System.Threading;
using System.Threading.Tasks;
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

    public static QueuedMessageWorkItem Queued(string entryId = "1-0", int deliveryCount = 1) =>
        new(entryId, Message(), new NoOpWorkItemLease(), deliveryCount);

    public sealed class NoOpWorkItemLease : IWorkItemLease
    {
        public CancellationToken LostToken => CancellationToken.None;

        public Task<LeaseOperationResult> CompleteAsync(CancellationToken cancellationToken) =>
            Task.FromResult(LeaseOperationResult.Applied);

        public Task<LeaseOperationResult> RetryAsync(Exception exception, CancellationToken cancellationToken) =>
            Task.FromResult(LeaseOperationResult.Applied);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
