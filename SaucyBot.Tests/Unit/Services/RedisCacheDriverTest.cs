using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SaucyBot.Library.Sites.ExHentai;
using SaucyBot.Library.Sites.HentaiFoundry;
using SaucyBot.Library.Sites.Newgrounds;
using SaucyBot.Options;
using SaucyBot.Services.Cache;
using Xunit;

namespace SaucyBot.Tests.Unit.Services;

public sealed class RedisCacheDriverTest
{
    [Fact]
    public async Task SetAndGetRoundTripsNestedResponseDto()
    {
        using var provider = CreateProvider();
        var driver = CreateDriver(provider);
        var expected = new CacheResponseDto(
            "illustration-123",
            new CacheResponseDetails(42, true),
            ["first", "second"],
            null);

        await driver.Set("typed-response", expected);
        var actual = await driver.Get<CacheResponseDto>("typed-response");

        Assert.NotNull(actual);
        Assert.Equal("illustration-123", actual.Id);
        Assert.Equal(42, actual.Details.Count);
        Assert.True(actual.Details.Enabled);
        Assert.Equal(["first", "second"], actual.Tags);
        Assert.Null(actual.Optional);
    }

    [Fact]
    public async Task SetAndGetPreservesNullOptionalProperties()
    {
        using var provider = CreateProvider();
        var driver = CreateDriver(provider);
        var expected = new CacheResponseDto("illustration-456", new CacheResponseDetails(0, false), [], null);

        await driver.Set("nullable-response", expected);
        var actual = await driver.Get<CacheResponseDto>("nullable-response");

        Assert.NotNull(actual);
        Assert.Null(actual.Optional);
        Assert.Empty(actual.Tags);
    }

    [Fact]
    public async Task SetAndGetRoundTripsParsedHtmlResultsWithoutRetainingDocuments()
    {
        using var provider = CreateProvider();
        var driver = CreateDriver(provider);
        var exHentai = new ExHentaiGalleryPage("<html><div class='gm'><h1 id='gn'>Cached gallery</h1></div></html>");
        var hentaiFoundry = new HentaiFoundryPicture("<html><h2 class='imageTitle'>Cached picture</h2></html>");
        var newgrounds = new NewgroundsArt("<html><div class='body-guts'><div class='column wide right'><div class='pod-head'><h2>Cached art</h2></div></div></div></html>");

        await driver.Set("parsed-exhentai", exHentai);
        await driver.Set("parsed-hentaifoundry", hentaiFoundry);
        await driver.Set("parsed-newgrounds", newgrounds);
        var actualExHentai = await driver.Get<ExHentaiGalleryPage>("parsed-exhentai");
        var actualHentaiFoundry = await driver.Get<HentaiFoundryPicture>("parsed-hentaifoundry");
        var actualNewgrounds = await driver.Get<NewgroundsArt>("parsed-newgrounds");

        Assert.Equal("Cached gallery", actualExHentai?.Title());
        Assert.Equal("Cached picture", actualHentaiFoundry?.Title());
        Assert.Equal("Cached art", actualNewgrounds?.Title());
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
        return services.BuildServiceProvider();
    }

    private static RedisCacheDriver CreateDriver(IServiceProvider provider) => new(
            provider.GetRequiredService<IDistributedCache>(),
            Microsoft.Extensions.Options.Options.Create(new CacheOptions()));

    public sealed record CacheResponseDto(
        string Id,
        CacheResponseDetails Details,
        List<string> Tags,
        string? Optional);

    public sealed record CacheResponseDetails(int Count, bool Enabled);
}
