using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SaucyBot.Library.Sites.ExHentai;
using SaucyBot.Library.Sites.HentaiFoundry;
using SaucyBot.Library.Sites.Newgrounds;
using SaucyBot.Services;
using Xunit;

namespace SaucyBot.Tests.Unit.Library.Sites;

public sealed class HtmlPageCacheClientTest
{
    [Fact]
    public async Task ExHentaiCachesParsedPageResult()
    {
        var cache = new CachingCacheManager();
        var handler = new RecordingHandler("""
            <html>
              <div class="gm">
                <h1 id="gn">Gallery title</h1>
                <div id="comment_0">Gallery description</div>
                <table><tr><td id="rating_label">Average: 4.5</td></tr></table>
                <div id="gd1"><div style="url(https://images.example/gallery.jpg)"></div></div>
                <div id="gmid">
                  <div id="gdn"><a href="https://exhentai.org/uploader/artist">Gallery artist</a></div>
                  <div id="gd3"><div id="gdd"><table><tbody>
                    <tr><td>Language:</td><td>English</td></tr>
                    <tr><td>Length:</td><td>20 pages</td></tr>
                    <tr><td>Posted:</td><td>2024-01-01 00:00</td></tr>
                  </tbody></table></div></div>
                </div>
              </div>
            </html>
            """);
        var client = new ExHentaiClient(
            Substitute.For<ILogger<ExHentaiClient>>(),
            new ConfigurationBuilder().Build(),
            cache,
            new HttpClient(handler));
        var request = new ExHentaiGalleryRequest(ExHentaiRequestMode.EHentai, "123", "hash");

        var first = await client.GetGallery(request);
        var second = await client.GetGallery(request);

        Assert.NotNull(first);
        Assert.Equal("Gallery title", first.Title());
        Assert.Equal("Gallery description", first.Description());
        Assert.Equal("4.5", first.Rating());
        Assert.Equal("English", first.Language());
        Assert.Equal("20", first.Length());
        Assert.Equal("Gallery artist", first.AuthorName());
        Assert.Equal("https://exhentai.org/uploader/artist", first.AuthorUrl());
        Assert.Equal("https://images.example/gallery.jpg", first.ImageUrl());
        var expectedLocalOffset = TimeZoneInfo.Local.GetUtcOffset(new DateTime(2024, 1, 1));
        Assert.Equal(new DateTimeOffset(2024, 1, 1, 0, 0, 0, expectedLocalOffset), first.PostedAt());
        Assert.Equal("Gallery title", second?.Title());
        Assert.Equal(typeof(ExHentaiGalleryPage), cache.LastRememberType);
        Assert.Equal(1, handler.RequestCount);

        var incomplete = new ExHentaiGalleryPage("<html></html>");
        Assert.Null(incomplete.Title());
        Assert.Null(incomplete.ImageUrl());
        Assert.Null(incomplete.PostedAt());
    }

    [Fact]
    public async Task HentaiFoundryCachesParsedPageResult()
    {
        var cache = new CachingCacheManager();
        var handler = new RecordingHandler("""
            <html>
              <h2 class="imageTitle">Picture title</h2>
              <div class="picDescript">Picture description</div>
              <div id="picBox"><div class="boxbody"><img src="//images.example/picture.jpg"></div></div>
              <div id="descriptionBox"><div class="boxbody"><a href="/user/profile">Profile</a><a href="/user/artist"><img title="Picture artist" src="//images.example/avatar.jpg"></a></div></div>
              <div id="pictureGeneralInfoBox">
                <time datetime="2025-05-06T07:08:09Z"></time>
                <div class="boxbody">
                  <div class="column"><span>Views</span> 91</div>
                  <div class="column"><span>Vote Score</span> 8</div>
                </div>
              </div>
            </html>
            """);
        var client = new HentaiFoundryClient(
            Substitute.For<ILogger<HentaiFoundryClient>>(),
            new ConfigurationBuilder().Build(),
            cache,
            new HttpClient(handler));

        var first = await client.GetPage("https://www.hentai-foundry.com/pictures/test");
        var second = await client.GetPage("https://www.hentai-foundry.com/pictures/test");

        Assert.NotNull(first);
        Assert.Equal("Picture title", first.Title());
        Assert.Equal("Picture description", first.Description());
        Assert.Equal("https://images.example/picture.jpg", first.ImageUrl());
        Assert.Equal("Picture artist", first.AuthorName());
        Assert.Equal("https://www.hentai-foundry.com/user/profile", first.AuthorUrl());
        Assert.Equal("https://images.example/avatar.jpg", first.AuthorAvatarUrl());
        Assert.Equal(new DateTimeOffset(2025, 5, 6, 7, 8, 9, TimeSpan.Zero), first.PostedAt());
        Assert.Equal("91", first.Views());
        Assert.Equal("8", first.Votes());
        Assert.Equal("Picture title", second?.Title());
        Assert.Equal(typeof(HentaiFoundryPicture), cache.LastRememberType);
        Assert.Equal(1, handler.RequestCount);

        var incomplete = new HentaiFoundryPicture("<html></html>");
        Assert.Null(incomplete.Title());
        Assert.Null(incomplete.ImageUrl());
        Assert.Equal("0", incomplete.Views());
        Assert.Equal("0", incomplete.Votes());
    }

    [Fact]
    public async Task NewgroundsCachesParsedPageResult()
    {
        var cache = new CachingCacheManager();
        var handler = new RecordingHandler("""
            <html>
              <div class="body-guts">
                <div class="column wide right"><div class="pod-head"><h2>Art title</h2></div></div>
              </div>
              <div id="author_comments"><p>Artist description</p></div>
              <div class="pod-body"><div class="image"><img src="https://images.example/art.jpg"></div></div>
              <dl class="sidestats"><dt>Views</dt><dd>34</dd></dl>
              <span id="score_number">4.25</span>
            </html>
            """);
        var client = new NewgroundsClient(
            Substitute.For<ILogger<NewgroundsClient>>(),
            new ConfigurationBuilder().Build(),
            cache,
            new HttpClient(handler));

        var first = await client.GetArt("artist", "slug");
        var second = await client.GetArt("artist", "slug");

        Assert.NotNull(first);
        Assert.Equal("Art title", first.Title());
        Assert.Equal("<p>Artist description</p>", first.Description());
        Assert.Equal("https://images.example/art.jpg", first.ImageUrl());
        Assert.Equal("34", first.Views());
        Assert.Equal("4.25", first.Score());
        Assert.Equal("Art title", second?.Title());
        Assert.Equal(typeof(NewgroundsArt), cache.LastRememberType);
        Assert.Equal(1, handler.RequestCount);

        var incomplete = new NewgroundsArt("<html></html>");
        Assert.Null(incomplete.Title());
        Assert.Null(incomplete.ImageUrl());
        Assert.Equal("0", incomplete.Views());
        Assert.Equal("0.00", incomplete.Score());
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
