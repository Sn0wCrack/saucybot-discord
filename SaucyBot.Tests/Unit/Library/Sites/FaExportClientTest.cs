using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SaucyBot.Library.Sites.FurAffinity;
using SaucyBot.Services;
using Xunit;

namespace SaucyBot.Tests.Unit.Library.Sites;

public sealed class FaExportClientTest
{
    [Fact]
    public async Task GetSubmissionCachesTypedSubmissionWithCleanedDescription()
    {
        var cache = new CachingCacheManager();
        var handler = new RecordingHandler("""
            {
              "title": "Submission title",
              "description": "<div class='bbcode_quote'><p class='bbcode_bold'>Quoted text</p></div>",
              "description_body": "",
              "name": "Artist",
              "profile": "https://www.furaffinity.net/user/artist/",
              "profile_name": "artist",
              "avatar": "https://a.furaffinity.net/avatar.gif",
              "link": "https://www.furaffinity.net/view/123/",
              "posted": "Jan 01, 2024 12:00 AM",
              "posted_at": "2024-01-01T00:00:00Z",
              "download": "https://d.furaffinity.net/image.png",
              "full": "https://d.furaffinity.net/image.png",
              "thumbnail": "https://t.furaffinity.net/image.jpg",
              "category": "Art",
              "theme": "Original",
              "species": "Unknown",
              "gender": "Unknown",
              "favorites": "1",
              "comments": "2",
              "views": "3",
              "resolution": "100x100",
              "rating": "general",
              "keywords": []
            }
            """);
        var client = new FaExportClient(cache, new HttpClient(handler));

        var first = await client.GetSubmission("123");
        var second = await client.GetSubmission("123");

        Assert.NotNull(first);
        Assert.Equal("<blockquote><p class=\"\">Quoted text</p></blockquote>", first.Description);
        Assert.Equal(first.Description, second?.Description);
        Assert.Equal(typeof(FaExportSubmission), cache.LastRememberType);
        Assert.Equal("furaffinity.post_123", cache.LastKey);
        Assert.Equal(1, handler.RequestCount);
    }

    private sealed class RecordingHandler(string response) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response),
                RequestMessage = request,
            });
        }
    }

    private sealed class CachingCacheManager : ICacheManager
    {
        private readonly Dictionary<object, object?> _values = [];

        public Type? LastRememberType { get; private set; }
        public object? LastKey { get; private set; }

        public Task<T?> Get<T>(object key) => Task.FromResult(
            _values.TryGetValue(key, out var value) ? (T?)value : default);

        public Task<T> Set<T>(object key, T value)
        {
            _values[key] = value;
            return Task.FromResult(value);
        }

        public Task<T> Set<T>(object key, T value, TimeSpan expiry) => Set(key, value);
        public Task<bool> Delete(object key) => Task.FromResult(_values.Remove(key));
        public Task<T?> Remember<T>(object key, Func<Task<T?>> value) => RememberCore(key, value);
        public Task<T?> Remember<T>(object key, TimeSpan expiry, Func<Task<T?>> value) => RememberCore(key, value);

        private async Task<T?> RememberCore<T>(object key, Func<Task<T?>> value)
        {
            LastRememberType = typeof(T);
            LastKey = key;

            var existing = await Get<T>(key);
            if (existing is not null)
            {
                return existing;
            }

            var created = await value();
            if (created is not null)
            {
                await Set(key, created);
            }

            return created;
        }
    }
}
