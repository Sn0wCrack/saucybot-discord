using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using NSubstitute;
using SaucyBot.Queue;
using SaucyBot.Site;
using Xunit;

namespace SaucyBot.Tests.Unit.Site;

public sealed class MessageContextTest
{
    [Fact]
    public async Task QueuedContextPreservesSerializedEmbedsWithoutResolvingTheMessage()
    {
        var resolver = Substitute.For<IMessageResolver>();
        var item = CreateItem([new MessageEmbed("title", "description", "https://example.test")]);

        var context = new QueuedMessageContext(item, resolver);

        var embeds = await context.GetLatestEmbedsAsync(TestContext.Current.CancellationToken);

        Assert.Single(embeds);
        Assert.Equal("title", embeds[0].Title);
        resolver.DidNotReceiveWithAnyArgs().GetCachedMessage(default, default);
        await resolver.DidNotReceiveWithAnyArgs().FetchMessageAsync(default, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public void QueuedContextIncludesForwardedContentInMatchingText()
    {
        var item = CreateItem([]) with { Content = "primary", ForwardedContent = "forwarded link" };
        var context = new QueuedMessageContext(item, Substitute.For<IMessageResolver>());

        Assert.Equal("primary\nforwarded link", context.AllMessageContent);
    }

    [Fact]
    public async Task QueuedContextUsesCachedMessageBeforeRestWhenSerializedEmbedsAreEmpty()
    {
        var resolver = Substitute.For<IMessageResolver>();
        var cached = Substitute.For<IUserMessage>();
        cached.Embeds.Returns([new EmbedBuilder { Title = "updated" }.Build()]);
        resolver.GetCachedMessage(42, 7).Returns(cached);
        var item = CreateItem([]);

        var context = new QueuedMessageContext(item, resolver);

        var embeds = await context.GetLatestEmbedsAsync(TestContext.Current.CancellationToken);

        Assert.Single(embeds);
        Assert.Equal("updated", embeds[0].Title);
        await resolver.DidNotReceiveWithAnyArgs().FetchMessageAsync(default, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task QueuedContextPerformsAtMostOneTargetedFetchWhenCacheIsMissing()
    {
        var resolver = Substitute.For<IMessageResolver>();
        resolver.GetCachedMessage(42, 7).Returns((IUserMessage?)null);
        var fetched = Substitute.For<IUserMessage>();
        fetched.Embeds.Returns([new EmbedBuilder { Title = "fetched" }.Build()]);
        resolver.FetchMessageAsync(42, 7, Arg.Any<CancellationToken>()).Returns(fetched);
        var item = CreateItem([]);
        var context = new QueuedMessageContext(item, resolver);

        var first = await context.GetLatestEmbedsAsync(TestContext.Current.CancellationToken);
        var second = await context.GetLatestEmbedsAsync(TestContext.Current.CancellationToken);

        Assert.Equal("fetched", first.Single().Title);
        Assert.Equal("fetched", second.Single().Title);
        await resolver.Received(1).FetchMessageAsync(42, 7, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LiveContextRereadsTheCurrentCacheAcrossSuccessiveCalls()
    {
        var message = (SocketUserMessage)RuntimeHelpers.GetUninitializedObject(typeof(SocketUserMessage));

        var resolver = Substitute.For<IMessageResolver>();
        var initial = Substitute.For<IUserMessage>();
        initial.Embeds.Returns([new EmbedBuilder { Title = "initial" }.Build()]);
        var updated = Substitute.For<IUserMessage>();
        updated.Embeds.Returns([new EmbedBuilder { Title = "updated" }.Build()]);
        resolver.GetCachedMessage(0, 0).Returns(initial, updated);

        var context = new DiscordMessageContext(
            message,
            resolver,
            [new EmbedBuilder { Title = "initial" }.Build()]);

        var initialEmbeds = await context.GetLatestEmbedsAsync(TestContext.Current.CancellationToken);
        var updatedEmbeds = await context.GetLatestEmbedsAsync(TestContext.Current.CancellationToken);

        Assert.Equal("initial", initialEmbeds.Single().Title);
        Assert.Equal("updated", updatedEmbeds.Single().Title);
        await resolver.DidNotReceiveWithAnyArgs().FetchMessageAsync(default, default, TestContext.Current.CancellationToken);
    }

    private static MessageWorkItem CreateItem(IReadOnlyList<MessageEmbed> embeds) => new(
        7,
        99,
        42,
        12,
        [],
        "https://example.test",
        null,
        embeds,
        true,
        true,
        Guid.NewGuid());
}
