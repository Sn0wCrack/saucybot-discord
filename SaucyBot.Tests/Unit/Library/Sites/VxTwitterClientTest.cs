using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SaucyBot.Library.Sites.Twitter;
using SaucyBot.Services;
using Xunit;

namespace SaucyBot.Tests.Unit.Library.Sites;

public sealed class VxTwitterClientTest
{
    [Fact]
    public async Task GetTweetCallsVxTwitterApiAndDeserializesDocumentedResponse()
    {
        var handler = new RecordingHandler("""
            {
              "date": "Wed Oct 05 18:40:30 +0000 2022",
              "date_epoch": 1664995230,
              "hashtags": ["so", "cool"],
              "likes": 21664,
              "mediaURLs": ["https://pbs.twimg.com/media/example.jpg"],
              "media_extended": [{
                "altText": "an example",
                "size": { "height": 1007, "width": 1179 },
                "thumbnail_url": "https://pbs.twimg.com/media/example.jpg",
                "type": "image",
                "url": "https://pbs.twimg.com/media/example.jpg"
              }],
              "replies": 2911,
              "retweets": 3229,
              "text": "hello",
              "tweetID": "1577730467436138524",
              "tweetURL": "https://twitter.com/Twitter/status/1577730467436138524",
              "user_name": "Twitter",
              "user_screen_name": "Twitter"
            }
            """);
        var client = new VxTwitterClient(
            Substitute.For<ILogger<VxTwitterClient>>(),
            new PassthroughCacheManager(),
            new HttpClient(handler));

        var result = await client.GetTweet("Twitter", "1577730467436138524", includeRtf: "true", includeTxt: "ifnomedia");

        Assert.NotNull(result);
        Assert.Equal("1577730467436138524", result.TweetId);
        Assert.Equal(1664995230, result.DateEpoch);
        Assert.Equal(["so", "cool"], result.Hashtags);
        Assert.Equal("example.jpg", result.MediaExtended[0].Url.Split('/').Last());
        Assert.Equal("image", result.MediaExtended[0].Type);
        Assert.Equal("Twitter", result.UserScreenName);
        Assert.Equal("https://api.vxtwitter.com/Twitter/status/1577730467436138524?include_rtf=true&include_txt=ifnomedia", handler.RequestUri?.ToString());
    }

    [Fact]
    public async Task GetTweetReturnsNullWhenTheApiReturnsAnError()
    {
        var client = new VxTwitterClient(
            Substitute.For<ILogger<VxTwitterClient>>(),
            new PassthroughCacheManager(),
            new HttpClient(new RecordingHandler("", HttpStatusCode.Forbidden)));

        var result = await client.GetTweet("Twitter", "1577730467436138524");

        Assert.Null(result);
    }

    private sealed class RecordingHandler(string response, HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(response),
                RequestMessage = request,
            });
        }
    }

    private sealed class PassthroughCacheManager : ICacheManager
    {
        public Task<T?> Get<T>(object key) => Task.FromResult<T?>(default);
        public Task<T> Set<T>(object key, T value) => Task.FromResult(value);
        public Task<T> Set<T>(object key, T value, TimeSpan expiry) => Task.FromResult(value);
        public Task<bool> Delete(object key) => Task.FromResult(false);
        public Task<T?> Remember<T>(object key, Func<Task<T?>> value) => value();
        public Task<T?> Remember<T>(object key, TimeSpan expiry, Func<Task<T?>> value) => value();
    }
}
