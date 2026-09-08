using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SaucyBot.Library.Sites.Twitter;
using SaucyBot.Site;
using SaucyBot.Site.Twitter;
using Xunit;

namespace SaucyBot.Tests.Unit.Site;

public class FxTwitterTest
{
    [Fact]
    public async Task GetFileRejectsUnsuccessfulResponses()
    {
        var content = new TrackingContent();
        var site = CreateDownloadSite(new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = content,
        }));

        await Assert.ThrowsAsync<HttpRequestException>(() => site.GetFile("https://example.test/image.jpg", TestContext.Current.CancellationToken));

        Assert.Equal(1, content.DisposeCount);
    }

    [Fact]
    public async Task GetFilePassesCancellationToTheHttpRequest()
    {
        var cancellation = new CancellationTokenSource();
        var handler = new CancellationHandler();
        var site = CreateDownloadSite(handler);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => site.GetFile("https://example.test/image.jpg", cancellation.Token));

        Assert.True(handler.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task PokeFileRejectsUnsuccessfulResponses()
    {
        var content = new TrackingContent();
        var site = CreateDownloadSite(new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = content,
        }));

        await Assert.ThrowsAsync<HttpRequestException>(() => site.PokeFile("https://example.test/image.jpg", TestContext.Current.CancellationToken));

        Assert.Equal(1, content.DisposeCount);
    }

    [Fact]
    public async Task AnEmbedIsCreatedForTweet()
    {
        var logger = Substitute.For<ILogger<FxTwitterSite>>();
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

        var site = new FxTwitterSite(logger, config.FxTwitterOptions(), client, httpClientFactory);

        var matches = site.Pattern.Matches("https://twitter.com/testuser/status/123456789");
        var match = matches[0];

        var result = await site.Process(new ProcessRequest(match));

        Assert.NotNull(result);
        Assert.NotEmpty(result.Embeds);
        Assert.Single(result.Embeds);
    }

    [Fact]
    public async Task NothingIsReturnedWhenTheApiClientReturnsUnsuccessfully()
    {
        var logger = Substitute.For<ILogger<FxTwitterSite>>();
        var config = new ConfigurationBuilder()
            .Build();
        var client = Substitute.For<IFxTwitterClient>();
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(new HttpClient());

        client
            .GetTweet(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>())
            .Returns((FxTwitterResponse?)null);

        var site = new FxTwitterSite(logger, config.FxTwitterOptions(), client, httpClientFactory);

        var matches = site.Pattern.Matches("https://twitter.com/testuser/status/123456789");
        var match = matches[0];

        var result = await site.Process(new ProcessRequest(match));

        Assert.Null(result);
    }

    [Fact]
    public async Task HandlesTweetWithMedia()
    {
        var logger = Substitute.For<ILogger<FxTwitterSite>>();
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

        var site = new FxTwitterSite(logger, config.FxTwitterOptions(), client, httpClientFactory);

        var matches = site.Pattern.Matches("https://twitter.com/testuser/status/123456789");
        var match = matches[0];

        var result = await site.Process(new ProcessRequest(match));

        Assert.NotNull(result);
        Assert.NotEmpty(result.Embeds);
        Assert.Equal(2, result.Embeds.Count);
    }

    [Fact]
    public void MultipleTweetsOnTheSameLineAreAllMatched()
    {
        var logger = Substitute.For<ILogger<FxTwitterSite>>();
        var config = new ConfigurationBuilder()
            .Build();
        var client = Substitute.For<IFxTwitterClient>();
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(new HttpClient());

        var site = new FxTwitterSite(logger, config.FxTwitterOptions(), client, httpClientFactory);

        var matches = site.Pattern.Matches("https://twitter.com/alice/status/111 https://twitter.com/bob/status/222");

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
        var logger = Substitute.For<ILogger<FxTwitterSite>>();
        var config = new ConfigurationBuilder()
            .Build();
        var client = Substitute.For<IFxTwitterClient>();
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(new HttpClient());

        var site = new FxTwitterSite(logger, config.FxTwitterOptions(), client, httpClientFactory);

        var matches = site.Pattern.Matches(content);

        Assert.Equal(2, matches.Count);

        Assert.Equal("testuser123", matches[0].Groups["user"].Value);
        Assert.Equal("2072717186859471548", matches[0].Groups["id"].Value);

        Assert.Equal("testuser456", matches[1].Groups["user"].Value);
        Assert.Equal("2070445370250928375", matches[1].Groups["id"].Value);
    }

    [Fact]
    public void TweetsOnSeparateLinesBeforeAndAfterSameLineTweetsAreAllMatched()
    {
        var logger = Substitute.For<ILogger<FxTwitterSite>>();
        var config = new ConfigurationBuilder()
            .Build();
        var client = Substitute.For<IFxTwitterClient>();
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(new HttpClient());

        var site = new FxTwitterSite(logger, config.FxTwitterOptions(), client, httpClientFactory);

        var content =
            "https://x.com/first/status/1\n" +
            "https://x.com/second/status/2 https://x.com/third/status/3\n" +
            "https://x.com/fourth/status/4";

        var matches = site.Pattern.Matches(content);

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
        var logger = Substitute.For<ILogger<FxTwitterSite>>();
        var config = new ConfigurationBuilder()
            .Build();
        var client = Substitute.For<IFxTwitterClient>();
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(new HttpClient());

        var site = new FxTwitterSite(logger, config.FxTwitterOptions(), client, httpClientFactory);

        var content = "check this https://x.com/first/status/1 out and also https://x.com/second/status/2 lol";

        var matches = site.Pattern.Matches(content);

        Assert.Equal(2, matches.Count);

        Assert.Equal("first", matches[0].Groups["user"].Value);
        Assert.Equal("1", matches[0].Groups["id"].Value);

        Assert.Equal("second", matches[1].Groups["user"].Value);
        Assert.Equal("2", matches[1].Groups["id"].Value);
    }

    private static FxTwitterSite CreateDownloadSite(HttpMessageHandler handler)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(new HttpClient(handler));
        return new FxTwitterSite(
            Substitute.For<ILogger<FxTwitterSite>>(),
            new ConfigurationBuilder().Build().FxTwitterOptions(),
            Substitute.For<IFxTwitterClient>(),
            factory);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request, cancellationToken));
    }

    private sealed class CancellationHandler : HttpMessageHandler
    {
        public CancellationToken CancellationToken { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CancellationToken = cancellationToken;
            return Task.FromCanceled<HttpResponseMessage>(cancellationToken);
        }
    }

    private sealed class TrackingContent : HttpContent
    {
        public int DisposeCount { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) => Task.CompletedTask;

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCount++;
            }

            base.Dispose(disposing);
        }
    }
}
