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

        await processor.ProcessAsync(CreateDelivery(), cancellation.Token);

        Assert.Equal(cancellation.Token, await observed.Task);
    }

    [Fact]
    public async Task ProcessingInvokesTheScopedHandlerWithTheDeliveryItem()
    {
        var delivery = CreateDelivery();
        MessageWorkItem? observed = null;
        var services = new ServiceCollection();
        services.AddScoped<IMessageWorkHandler>(_ => new DelegateMessageWorkHandler((item, _) =>
        {
            observed = item;
            return Task.CompletedTask;
        }));
        await using var provider = services.BuildServiceProvider();
        var processor = new WorkItemProcessor(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<WorkItemProcessor>.Instance);

        await processor.ProcessAsync(delivery, CancellationToken.None);

        Assert.Same(delivery.Item, observed);
    }

    [Fact]
    public async Task ProcessingFailuresPropagateToTheCaller()
    {
        var services = new ServiceCollection();
        services.AddScoped<IMessageWorkHandler>(_ => new DelegateMessageWorkHandler(
            (_, _) => throw new InvalidOperationException("handler failed")));
        await using var provider = services.BuildServiceProvider();
        var processor = new WorkItemProcessor(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<WorkItemProcessor>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => processor.ProcessAsync(CreateDelivery(), CancellationToken.None));
    }

    private static WorkDelivery<MessageWorkItem> CreateDelivery() => new(
        TestData.Message(),
        "1-0",
        1,
        DateTimeOffset.UtcNow,
        new TestData.NoOpWorkItemLease());

    private sealed class DelegateMessageWorkHandler(Func<MessageWorkItem, CancellationToken, Task> handler) : IMessageWorkHandler
    {
        public Task HandleAsync(MessageWorkItem item, CancellationToken cancellationToken) => handler(item, cancellationToken);
    }
}
