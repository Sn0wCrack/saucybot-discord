using System.Collections.Generic;
using System.Net.Http;
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
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(new HttpClient());

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

        var site = new FxTwitter(logger, config, client, httpClientFactory);

        var matches = site.Match("https://twitter.com/testuser/status/123456789");
        var match = matches[0];

        var result = await site.Process(new ProcessRequest(match));
        
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
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(new HttpClient());

        client
            .GetTweet(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>())
            .Returns((FxTwitterResponse?)null);

        var site = new FxTwitter(logger, config, client, httpClientFactory);

        var matches = site.Match("https://twitter.com/testuser/status/123456789");
        var match = matches[0];

        var result = await site.Process(new ProcessRequest(match));
        
        Assert.Null(result);
    }

    [Fact]
    public async Task HandlesTweetWithMedia()
    {
        var logger = Substitute.For<ILogger<FxTwitter>>();
        var config = new ConfigurationBuilder()
            .Build();
        var client = Substitute.For<IFxTwitterClient>();
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(new HttpClient());

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

        var site = new FxTwitter(logger, config, client, httpClientFactory);

        var matches = site.Match("https://twitter.com/testuser/status/123456789");
        var match = matches[0];

        var result = await site.Process(new ProcessRequest(match));

        Assert.NotNull(result);
        Assert.NotEmpty(result.Embeds);
        Assert.Equal(2, result.Embeds.Count);
    }

    [Fact]
    public void MultipleTweetsOnTheSameLineAreAllMatched()
    {
        var logger = Substitute.For<ILogger<FxTwitter>>();
        var config = new ConfigurationBuilder()
            .Build();
        var client = Substitute.For<IFxTwitterClient>();
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(new HttpClient());

        var site = new FxTwitter(logger, config, client, httpClientFactory);

        var matches = site.Match("https://twitter.com/alice/status/111 https://twitter.com/bob/status/222");

        Assert.Equal(2, matches.Count);
        Assert.Equal("111", matches[0].Groups["id"].Value);
        Assert.Equal("alice", matches[0].Groups["user"].Value);
        Assert.Equal("222", matches[1].Groups["id"].Value);
        Assert.Equal("bob", matches[1].Groups["user"].Value);
    }

    [Theory]
    [InlineData("https://x.com/testuser123/status/2072717186859471548 https://x.com/testuser456/status/2070445370250928375")]
    [InlineData("https://x.com/testuser123/status/2072717186859471548?t=123 https://x.com/testuser456/status/2070445370250928375")]
    [InlineData("https://x.com/testuser123/status/2072717186859471548 https://x.com/testuser456/status/2070445370250928375?t=123")]
    public void TwoTweetsOnTheSameLineAreBothMatchedRegardlessOfQueryParams(string content)
    {
        var logger = Substitute.For<ILogger<FxTwitter>>();
        var config = new ConfigurationBuilder()
            .Build();
        var client = Substitute.For<IFxTwitterClient>();
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(new HttpClient());

        var site = new FxTwitter(logger, config, client, httpClientFactory);

        var matches = site.Match(content);

        Assert.Equal(2, matches.Count);

        Assert.Equal("testuser123", matches[0].Groups["user"].Value);
        Assert.Equal("2072717186859471548", matches[0].Groups["id"].Value);

        Assert.Equal("testuser456", matches[1].Groups["user"].Value);
        Assert.Equal("2070445370250928375", matches[1].Groups["id"].Value);
    }

    [Fact]
    public void TweetsOnSeparateLinesBeforeAndAfterSameLineTweetsAreAllMatched()
    {
        var logger = Substitute.For<ILogger<FxTwitter>>();
        var config = new ConfigurationBuilder()
            .Build();
        var client = Substitute.For<IFxTwitterClient>();
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(new HttpClient());

        var site = new FxTwitter(logger, config, client, httpClientFactory);

        var content =
            "https://x.com/first/status/1\n" +
            "https://x.com/second/status/2 https://x.com/third/status/3\n" +
            "https://x.com/fourth/status/4";

        var matches = site.Match(content);

        Assert.Equal(4, matches.Count);

        Assert.Equal("first", matches[0].Groups["user"].Value);
        Assert.Equal("1", matches[0].Groups["id"].Value);

        Assert.Equal("second", matches[1].Groups["user"].Value);
        Assert.Equal("2", matches[1].Groups["id"].Value);

        Assert.Equal("third", matches[2].Groups["user"].Value);
        Assert.Equal("3", matches[2].Groups["id"].Value);

        Assert.Equal("fourth", matches[3].Groups["user"].Value);
        Assert.Equal("4", matches[3].Groups["id"].Value);
    }

    [Fact]
    public void TweetsSurroundedByTextOnASingleLineAreAllMatched()
    {
        var logger = Substitute.For<ILogger<FxTwitter>>();
        var config = new ConfigurationBuilder()
            .Build();
        var client = Substitute.For<IFxTwitterClient>();
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(new HttpClient());

        var site = new FxTwitter(logger, config, client, httpClientFactory);

        var content = "check this https://x.com/first/status/1 out and also https://x.com/second/status/2 lol";

        var matches = site.Match(content);

        Assert.Equal(2, matches.Count);

        Assert.Equal("first", matches[0].Groups["user"].Value);
        Assert.Equal("1", matches[0].Groups["id"].Value);

        Assert.Equal("second", matches[1].Groups["user"].Value);
        Assert.Equal("2", matches[1].Groups["id"].Value);
    }
}
