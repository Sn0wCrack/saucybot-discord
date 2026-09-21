using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SaucyBot.Library.Sites.Twitter;
using SaucyBot.Site;
using SaucyBot.Site.Twitter;
using Xunit;

namespace SaucyBot.Tests.Unit.Site;

public sealed class VxTwitterTest
{
    [Fact]
    public async Task AnEmbedIsCreatedForTweet()
    {
        var client = Substitute.For<IVxTwitterClient>();
        client.GetTweet(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(CreateTweet());
        var site = CreateSite(client);

        var result = await site.Process(CreateRequest(site, "https://twitter.com/testuser/status/123456789"));

        Assert.NotNull(result);
        var embed = Assert.Single(result.Embeds);
        Assert.Equal("Test tweet content", embed.Description);
        Assert.Equal("https://twitter.com/testuser/status/123456789", embed.Url);
        Assert.Equal("Test User (@testuser)", embed.Author?.Name);
    }

    [Fact]
    public async Task TweetMentionsAndHashtagsAreLinkifiedWithoutChangingUrls()
    {
        var client = Substitute.For<IVxTwitterClient>();
        client.GetTweet(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(CreateTweet(text: "@alice #saucy https://example.com/@not-a-mention"));
        var site = CreateSite(client);

        var result = await site.Process(CreateRequest(site, "https://twitter.com/testuser/status/123456789"));

        var description = Assert.Single(result!.Embeds).Description;
        Assert.Contains("[@alice](https://twitter.com/alice)", description);
        Assert.Contains("[#saucy](https://twitter.com/hashtag/saucy)", description);
        Assert.Contains("https://example.com/@not-a-mention", description);
    }

    [Fact]
    public async Task QuotedTweetMentionsAndHashtagsAreLinkified()
    {
        var client = Substitute.For<IVxTwitterClient>();
        client.GetTweet(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(CreateTweet(qrt: CreateTweet(text: "@quoted #topic")));
        var site = CreateSite(client);

        var result = await site.Process(CreateRequest(site, "https://twitter.com/testuser/status/123456789"));

        var description = Assert.Single(result!.Embeds).Description;
        Assert.Contains("> [@quoted](https://twitter.com/quoted) [#topic](https://twitter.com/hashtag/topic)", description);
    }

    [Fact]
    public async Task TweetImagesAreAddedAsEmbeds()
    {
        var client = Substitute.For<IVxTwitterClient>();
        client.GetTweet(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(CreateTweet(
                media: [
                    new VxTwitterMedia("first", null, new VxTwitterMediaSize(600, 800), "https://example.com/one.jpg", "image", "https://example.com/one.jpg"),
                    new VxTwitterMedia("second", null, new VxTwitterMediaSize(600, 800), "https://example.com/two.jpg", "image", "https://example.com/two.jpg")
                ]));
        var site = CreateSite(client);

        var result = await site.Process(CreateRequest(site, "https://x.com/testuser/status/123456789"));

        Assert.NotNull(result);
        Assert.Equal(2, result.Embeds.Count);
        Assert.Equal("https://example.com/one.jpg", result.Embeds[0].Image?.Url);
        Assert.Equal("https://example.com/two.jpg", result.Embeds[1].Image?.Url);
    }

    [Fact]
    public async Task VideoTweetFallsBackToVxTwitterLink()
    {
        var client = Substitute.For<IVxTwitterClient>();
        client.GetTweet(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(CreateTweet(media: [
                new VxTwitterMedia(null, 1000, new VxTwitterMediaSize(600, 800), "https://example.com/video.jpg", "video", "https://example.com/video.mp4")
            ]));
        var site = CreateSite(client);

        var result = await site.Process(CreateRequest(site, "https://twitter.com/testuser/status/123456789"));

        Assert.NotNull(result);
        Assert.Empty(result.Embeds);
        Assert.Equal("https://vxtwitter.com/testuser/status/123456789", result.Text);
    }

    [Fact]
    public async Task NothingIsReturnedWhenTheApiClientReturnsUnsuccessfully()
    {
        var client = Substitute.For<IVxTwitterClient>();
        client.GetTweet(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns((VxTwitterResponse?)null);
        var site = CreateSite(client);

        var result = await site.Process(CreateRequest(site, "https://twitter.com/testuser/status/123456789"));

        Assert.Null(result);
    }

    [Fact]
    public async Task QuoteTweetTextIsIncludedInTheDescription()
    {
        var client = Substitute.For<IVxTwitterClient>();
        client.GetTweet(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(CreateTweet(qrt: CreateTweet(text: "Quoted tweet content")));
        var site = CreateSite(client);

        var result = await site.Process(CreateRequest(site, "https://twitter.com/testuser/status/123456789"));

        var embed = Assert.Single(result!.Embeds);
        Assert.Contains("> **[Quoting](https://twitter.com/testuser/status/123456789) Test User ([@testuser](https://twitter.com/testuser))**", embed.Description);
        Assert.Contains("> Quoted tweet content", embed.Description);
    }

    [Fact]
    public async Task QuotedTweetImagesAreAddedWhenTheMainTweetHasNoMedia()
    {
        var client = Substitute.For<IVxTwitterClient>();
        client.GetTweet(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(CreateTweet(qrt: CreateTweet(media: [
                new VxTwitterMedia("quoted", null, new VxTwitterMediaSize(600, 800), "https://example.com/quoted.jpg", "image", "https://example.com/quoted.jpg")
            ])));
        var site = CreateSite(client);

        var result = await site.Process(CreateRequest(site, "https://twitter.com/testuser/status/123456789"));

        var embed = Assert.Single(result!.Embeds);
        Assert.Equal("https://example.com/quoted.jpg", embed.Image?.Url);
    }

    [Fact]
    public async Task AnyQuotedVideoForcesTheVxTwitterLinkFallback()
    {
        var client = Substitute.For<IVxTwitterClient>();
        client.GetTweet(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(CreateTweet(
                media: [
                    new VxTwitterMedia("main", null, new VxTwitterMediaSize(600, 800), "https://example.com/main.jpg", "image", "https://example.com/main.jpg")
                ],
                qrt: CreateTweet(media: [
                    new VxTwitterMedia(null, 1000, new VxTwitterMediaSize(600, 800), "https://example.com/quoted-video.jpg", "video", "https://example.com/quoted-video.mp4")
                ])));
        var site = CreateSite(client);

        var result = await site.Process(CreateRequest(site, "https://twitter.com/testuser/status/123456789"));

        Assert.NotNull(result);
        Assert.Empty(result.Embeds);
        Assert.Equal("https://vxtwitter.com/testuser/status/123456789", result.Text);
    }

    private static VxTwitterSite CreateSite(IVxTwitterClient client) =>
        new(Substitute.For<ILogger<VxTwitterSite>>(), client);

    private static ProcessRequest CreateRequest(VxTwitterSite site, string url) =>
        new(site.Pattern.Match(url));

    private static VxTwitterResponse CreateTweet(
        List<VxTwitterMedia>? media = null,
        VxTwitterResponse? qrt = null,
        string text = "Test tweet content") =>
        new(
            "Mon Jan 01 00:00:00 +0000 2024",
            1704067200,
            ["test"],
            20,
            media?.ConvertAll(item => item.Url) ?? [],
            media ?? [],
            false,
            5,
            10,
            text,
            "123456789",
            "https://twitter.com/testuser/status/123456789",
            "Test User",
            "testuser",
            "",
            qrt);
}
