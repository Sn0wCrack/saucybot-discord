using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SaucyBot.Library.Sites.Pixiv;
using SaucyBot.Services;
using Xunit;

namespace SaucyBot.Tests.Unit.Library.Sites;

public sealed class PixivClientTest
{
    [Fact]
    public async Task IllustrationDetailsCachesTheTypedResponse()
    {
        var cache = new RecordingCacheManager();
        var client = CreateClient(cache, """
            {
              "error": false,
              "message": "",
              "body": {
                "id": "123",
                "title": "A title",
                "description": "",
                "illustType": 0,
                "aiType": 0,
                "urls": { "mini": "mini", "thumb": "thumb", "small": "small", "regular": "regular", "original": "original" },
                "likeCount": 1,
                "bookmarkCount": 2,
                "viewCount": 3,
                "pageCount": 1,
                "userId": "456",
                "userName": "artist",
                "userAccount": "artist-account",
                "xRestrict": 0,
                "createDate": "2024-01-01T00:00:00Z",
                "uploadDate": "2024-01-01T00:00:00Z"
              }
            }
            """);

        var result = await client.IllustrationDetails("123");

        Assert.NotNull(result);
        Assert.Equal("123", result.IllustrationDetails.Id);
        Assert.Equal(typeof(IllustrationDetailsResponse), cache.LastRememberType);
        Assert.Equal("pixiv.illustration_details_123", cache.LastKey);
    }

    [Fact]
    public async Task UserDetailsPreservesSevenDayCacheLifetime()
    {
        var cache = new RecordingCacheManager();
        var client = CreateClient(cache, """
            {
              "body": {
                "userId": "456",
                "image": "avatar-small",
                "imageBig": "avatar-large"
              }
            }
            """);

        var result = await client.UserDetails("456");

        Assert.NotNull(result);
        Assert.Equal("456", result.User.UserId);
        Assert.Equal(typeof(UserDetailsResponse), cache.LastRememberType);
        Assert.Equal("pixiv.user_456", cache.LastKey);
        Assert.Equal(TimeSpan.FromDays(7), cache.LastExpiry);
    }

    private static PixivClient CreateClient(RecordingCacheManager cache, string response) => new(
        Substitute.For<ILogger<PixivClient>>(),
        new ConfigurationBuilder().Build(),
        cache,
        new HttpClient(new StaticResponseHandler(response)));

    private sealed class StaticResponseHandler(string response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response),
                RequestMessage = request,
            });
    }

    private sealed class RecordingCacheManager : ICacheManager
    {
        public Type? LastRememberType { get; private set; }
        public object? LastKey { get; private set; }
        public TimeSpan? LastExpiry { get; private set; }

        public Task<T?> Get<T>(object key) => Task.FromResult<T?>(default);
        public Task<T> Set<T>(object key, T value) => Task.FromResult(value);
        public Task<T> Set<T>(object key, T value, TimeSpan expiry) => Task.FromResult(value);
        public Task<bool> Delete(object key) => Task.FromResult(false);

        public Task<T?> Remember<T>(object key, Func<Task<T?>> value)
        {
            LastRememberType = typeof(T);
            LastKey = key;
            LastExpiry = null;
            return value();
        }

        public Task<T?> Remember<T>(object key, TimeSpan expiry, Func<Task<T?>> value)
        {
            LastRememberType = typeof(T);
            LastKey = key;
            LastExpiry = expiry;
            return value();
        }
    }
}
