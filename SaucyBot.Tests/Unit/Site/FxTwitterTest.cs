using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SaucyBot.Library.Sites.Twitter;
using SaucyBot.Site;
using NSubstitute;
using Xunit;

namespace SaucyBot.Tests.Unit.Site;

public class FxTwitterTest
{
    [Fact]
    public async Task AnEmbedIsCreatedForTweet()
    {
        var logger = Substitute.For<ILogger<FxTwitter>>();
        var config = new ConfigurationBuilder()
            .Build();
        var client = Substitute.For<IFxTwitterClient>();

        var tweet = new FxTwitterTweet(
            Id: "123456789",
            Url: "https://twitter.com/testuser/status/123456789",
            Text: "Test tweet content",
            CreatedAt: "2024-01-01T00:00:00Z",
            CreatedTimestamp: 1704067200,
            Author: new FxTwitterAuthor("123", "Test User", "testuser", "https://example.com/avatar.jpg", "https://twitter.com/testuser", null, null),
            Replies: 5,
            Retweets: 10,
            Likes: 20,
            Views: 100,
            Bookmarks: null,
            Color: null,
            TwitterCard: "summary",
            Language: null,
            Source: "web",
            PossiblySensitive: false,
            ReplyingToScreenName: null,
            ReplyingToStatusId: null,
            Translation: null,
            QuotedTweet: null,
            Poll: null,
            Media: null
        );

        var response = new FxTwitterResponse(200, "OK", tweet);

        client
            .GetTweet(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>())
            .Returns(response);

        var site = new FxTwitter(logger, config, client);

        var matches = site.Match("https://twitter.com/testuser/status/123456789");
        var match = matches[0];

        var result = await site.Process(match);
        
        Assert.NotNull(result);
        Assert.NotEmpty(result.Embeds);
        Assert.Single(result.Embeds);
    }

    [Fact]
    public async Task NothingIsReturnedWhenTheApiClientReturnsUnsuccessfully()
    {
        var logger = Substitute.For<ILogger<FxTwitter>>();
        var config = new ConfigurationBuilder()
            .Build();
        var client = Substitute.For<IFxTwitterClient>();

        client
            .GetTweet(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>())
            .Returns((FxTwitterResponse?)null);

        var site = new FxTwitter(logger, config, client);

        var matches = site.Match("https://twitter.com/testuser/status/123456789");
        var match = matches[0];

        var result = await site.Process(match);
        
        Assert.Null(result);
    }

    [Fact]
    public async Task HandlesTweetWithMedia()
    {
        var logger = Substitute.For<ILogger<FxTwitter>>();
        var config = new ConfigurationBuilder()
            .Build();
        var client = Substitute.For<IFxTwitterClient>();

        var tweet = new FxTwitterTweet(
            Id: "123456789",
            Url: "https://twitter.com/testuser/status/123456789",
            Text: "Test tweet content",
            CreatedAt: "2024-01-01T00:00:00Z",
            CreatedTimestamp: 1704067200,
            Author: new FxTwitterAuthor("123", "Test User", "testuser", "https://example.com/avatar.jpg", "https://twitter.com/testuser", null, null),
            Replies: 5,
            Retweets: 10,
            Likes: 20,
            Views: 100,
            Bookmarks: null,
            Color: null,
            TwitterCard: "summary",
            Language: null,
            Source: "web",
            PossiblySensitive: false,
            ReplyingToScreenName: null,
            ReplyingToStatusId: null,
            Translation: null,
            QuotedTweet: null,
            Poll: null,
            Media: new FxTwitterMedia(
                new List<FxTwitterPhoto>
                {
                    new("photo", "https://example.com/image1.jpg", 800, 600),
                    new("photo", "https://example.com/image2.jpg", 800, 600),
                },
                null
            )
        );

        var response = new FxTwitterResponse(200, "OK", tweet);

        client
            .GetTweet(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>())
            .Returns(response);

        var site = new FxTwitter(logger, config, client);

        var matches = site.Match("https://twitter.com/testuser/status/123456789");
        var match = matches[0];

        var result = await site.Process(match);
        
        Assert.NotNull(result);
        Assert.NotEmpty(result.Embeds);
        Assert.Equal(2, result.Embeds.Count);
    }
}
