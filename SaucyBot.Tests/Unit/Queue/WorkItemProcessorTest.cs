using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
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
        var handler = Substitute.For<IMessageWorkHandler>();
        handler.HandleAsync(Arg.Any<MessageWorkItem>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                observed.TrySetResult(call.Arg<CancellationToken>());
                return Task.CompletedTask;
            });
        var services = new ServiceCollection();
        services.AddScoped<IMessageWorkHandler>(_ => handler);
        await using var provider = services.BuildServiceProvider();
        var processor = new WorkItemProcessor(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<WorkItemProcessor>.Instance);

        await processor.ProcessAsync(TestData.Message(), cancellation.Token);

        Assert.Equal(cancellation.Token, await observed.Task);
    }

    [Fact]
    public async Task ProcessingInvokesTheScopedHandlerWithTheDeliveryItem()
    {
        var item = TestData.Message();
        MessageWorkItem? observed = null;
        var handler = Substitute.For<IMessageWorkHandler>();
        handler.HandleAsync(Arg.Any<MessageWorkItem>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                observed = call.Arg<MessageWorkItem>();
                return Task.CompletedTask;
            });
        var services = new ServiceCollection();
        services.AddScoped<IMessageWorkHandler>(_ => handler);
        await using var provider = services.BuildServiceProvider();
        var processor = new WorkItemProcessor(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<WorkItemProcessor>.Instance);

        await processor.ProcessAsync(item, CancellationToken.None);

        Assert.Same(item, observed);
    }

    [Fact]
    public async Task ProcessingFailuresPropagateToTheCaller()
    {
        var handler = Substitute.For<IMessageWorkHandler>();
        handler.HandleAsync(Arg.Any<MessageWorkItem>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("handler failed")));
        var services = new ServiceCollection();
        services.AddScoped<IMessageWorkHandler>(_ => handler);
        await using var provider = services.BuildServiceProvider();
        var processor = new WorkItemProcessor(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<WorkItemProcessor>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => processor.ProcessAsync(TestData.Message(), CancellationToken.None));
    }

}
