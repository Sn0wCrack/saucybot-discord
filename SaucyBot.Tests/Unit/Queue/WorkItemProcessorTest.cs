using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SaucyBot.Queue;
using SaucyBot.Tests.Unit.Common;
using Xunit;

namespace SaucyBot.Tests.Unit.Queue;

public sealed class WorkItemProcessorTest
{
    [Fact]
    public async Task ProcessingPassesTheItemCancellationTokenToTheScopedHandler()
    {
        using var cancellation = new CancellationTokenSource();
        var observed = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new ServiceCollection();
        services.AddScoped<IMessageWorkHandler>(_ => new DelegateMessageWorkHandler((_, token) =>
        {
            observed.TrySetResult(token);
            return Task.CompletedTask;
        }));
        await using var provider = services.BuildServiceProvider();
        var processor = new WorkItemProcessor(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<WorkItemProcessor>.Instance);

        await processor.ProcessAsync(CreateItem(), cancellation.Token);

        Assert.Equal(cancellation.Token, await observed.Task);
    }

    private static QueuedMessageWorkItem CreateItem() => TestData.Queued();

    private sealed class DelegateMessageWorkHandler(Func<MessageWorkItem, CancellationToken, Task> handler) : IMessageWorkHandler
    {
        public Task HandleAsync(MessageWorkItem item, CancellationToken cancellationToken) => handler(item, cancellationToken);
    }
}
