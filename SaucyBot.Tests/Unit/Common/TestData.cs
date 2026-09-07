using System;
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

    public static QueuedMessageWorkItem Queued(string entryId = "1-0") =>
        new(entryId, Message());
}
