using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SaucyBot.Site;
using Xunit;

namespace SaucyBot.Tests.Unit.Site;

public class InstagramTest
{
    private readonly ILogger<Instagram> _logger;

    public InstagramTest()
    {
        _logger = Substitute.For<ILogger<Instagram>>();
    }

    // Positive Cases - Should Rewrite

    [Theory]
    [InlineData("https://instagram.com/p/ABC123/", "https://d.vxinstagram.com/p/ABC123/")]
    [InlineData("https://www.instagram.com/p/ABC123/", "https://d.vxinstagram.com/p/ABC123/")]
    [InlineData("https://m.instagram.com/p/ABC123/", "https://d.vxinstagram.com/p/ABC123/")]
    public async Task PostUrlsAreRewrittenCorrectly(string originalUrl, string expectedUrl)
    {
        var site = new Instagram(_logger);
        var match = site.Match(originalUrl).First();
        var response = await site.Process(match);

        Assert.NotNull(response);
        Assert.Equal(expectedUrl, response.Text);
    }
    
    [Theory]
    [InlineData("https://www.instagram.com/reel/DQLuwVcABa_/", "https://d.vxinstagram.com/reel/DQLuwVcABa_/")]
    [InlineData("https://www.instagram.com/reel/DQLu_wVcA_Ba/", "https://d.vxinstagram.com/reel/DQLu_wVcA_Ba/")]
    [InlineData("https://www.instagram.com/reel/DQLu_wVcA_Ba_/?utm_source=test", "https://d.vxinstagram.com/reel/DQLu_wVcA_Ba_/?utm_source=test")]
    public async Task ReelUrlsWithUnderscoresAreRewrittenCorrectly(string originalUrl, string expectedUrl)
    {
        var site = new Instagram(_logger);
        var match = site.Match(originalUrl).First();
        var response = await site.Process(match);

        Assert.NotNull(response);
        Assert.Equal(expectedUrl, response.Text);
    }

    [Theory]
    [InlineData("https://instagram.com/reel/EFG456/", "https://d.vxinstagram.com/reel/EFG456/")]
    [InlineData("https://www.instagram.com/reel/EFG456/", "https://d.vxinstagram.com/reel/EFG456/")]
    [InlineData("https://m.instagram.com/reel/EFG456/", "https://d.vxinstagram.com/reel/EFG456/")]
    public async Task ReelUrlsAreRewrittenCorrectly(string originalUrl, string expectedUrl)
    {
        var site = new Instagram(_logger);
        var match = site.Match(originalUrl).First();
        var response = await site.Process(match);

        Assert.NotNull(response);
        Assert.Equal(expectedUrl, response.Text);
    }

    [Theory]
    [InlineData("https://instagram.com/reels/HIJ789/", "https://d.vxinstagram.com/reels/HIJ789/")]
    [InlineData("https://www.instagram.com/reels/HIJ789/", "https://d.vxinstagram.com/reels/HIJ789/")]
    [InlineData("https://m.instagram.com/reels/HIJ789/", "https://d.vxinstagram.com/reels/HIJ789/")]
    public async Task ReelsUrlsAreRewrittenCorrectly(string originalUrl, string expectedUrl)
    {
        var site = new Instagram(_logger);
        var match = site.Match(originalUrl).First();
        var response = await site.Process(match);

        Assert.NotNull(response);
        Assert.Equal(expectedUrl, response.Text);
    }

    [Theory]
    [InlineData("https://instagram.com/p/ABC123/?igsh=MTIzZGFjYWQwYg==", "https://d.vxinstagram.com/p/ABC123/?igsh=MTIzZGFjYWQwYg==")]
    [InlineData("https://instagram.com/reel/EFG456/?utm_source=ig_web_copy_link", "https://d.vxinstagram.com/reel/EFG456/?utm_source=ig_web_copy_link")]
    [InlineData("https://instagram.com/reels/HIJ789/?igsh=xyz&utm_source=share", "https://d.vxinstagram.com/reels/HIJ789/?igsh=xyz&utm_source=share")]
    public async Task QueryParametersArePreserved(string originalUrl, string expectedUrl)
    {
        var site = new Instagram(_logger);
        var match = site.Match(originalUrl).First();
        var response = await site.Process(match);

        Assert.NotNull(response);
        Assert.Equal(expectedUrl, response.Text);
    }

    [Theory]
    [InlineData("https://instagram.com/p/ABC123/#anchor", "https://d.vxinstagram.com/p/ABC123/#anchor")]
    [InlineData("https://instagram.com/reel/EFG456/#comments", "https://d.vxinstagram.com/reel/EFG456/#comments")]
    [InlineData("https://instagram.com/reels/HIJ789/?igsh=xyz#frag", "https://d.vxinstagram.com/reels/HIJ789/?igsh=xyz#frag")]
    public async Task FragmentsArePreserved(string originalUrl, string expectedUrl)
    {
        var site = new Instagram(_logger);
        var match = site.Match(originalUrl).First();
        var response = await site.Process(match);

        Assert.NotNull(response);
        Assert.Equal(expectedUrl, response.Text);
    }

    [Theory]
    [InlineData("https://instagram.com/p/ABC123", "https://d.vxinstagram.com/p/ABC123")]
    [InlineData("https://instagram.com/p/ABC123/", "https://d.vxinstagram.com/p/ABC123/")]
    public async Task TrailingSlashesAreHandledCorrectly(string originalUrl, string expectedUrl)
    {
        var site = new Instagram(_logger);
        var match = site.Match(originalUrl).First();
        var response = await site.Process(match);

        Assert.NotNull(response);
        Assert.Equal(expectedUrl, response.Text);
    }

    [Theory]
    [InlineData("https://instagram.com/p/ABC123/extra/segment", "https://d.vxinstagram.com/p/ABC123/extra/segment")]
    [InlineData("https://instagram.com/reel/EFG456/another", "https://d.vxinstagram.com/reel/EFG456/another")]
    public async Task ExtraPathSegmentsArePreserved(string originalUrl, string expectedUrl)
    {
        var site = new Instagram(_logger);
        var match = site.Match(originalUrl).First();
        var response = await site.Process(match);

        Assert.NotNull(response);
        Assert.Equal(expectedUrl, response.Text);
    }

    [Theory]
    [InlineData("https://INSTAGRAM.COM/p/ABC123/", "https://d.vxinstagram.com/p/ABC123/")]
    [InlineData("HTTPS://Instagram.Com/reel/EFG456/", "https://d.vxinstagram.com/reel/EFG456/")]
    [InlineData("https://WWW.INSTAGRAM.COM/reels/HIJ789/", "https://d.vxinstagram.com/reels/HIJ789/")]
    public async Task SchemeAndHostAreCaseInsensitive(string originalUrl, string expectedUrl)
    {
        var site = new Instagram(_logger);
        var match = site.Match(originalUrl).First();
        var response = await site.Process(match);

        Assert.NotNull(response);
        Assert.Equal(expectedUrl, response.Text);
    }

    // Negative Cases - Should Not Match

    [Theory]
    [InlineData("https://d.vxinstagram.com/p/ABC123/")]
    [InlineData("https://d.vxinstagram.com/reel/EFG456/")]
    [InlineData("https://d.vxinstagram.com/reels/HIJ789/")]
    public void AlreadyRewrittenUrlsAreNotMatched(string url)
    {
        var site = new Instagram(_logger);
        var matches = site.Match(url);

        Assert.Empty(matches);
    }

    [Theory]
    [InlineData("https://instagram.com/stories/username/12345/")]
    [InlineData("https://instagram.com/accounts/login/")]
    [InlineData("https://instagram.com/oauth/authorize/")]
    [InlineData("https://instagram.com/challenge/")]
    [InlineData("https://instagram.com/direct/inbox/")]
    [InlineData("https://instagram.com/explore/")]
    public void NonSupportedEndpointsAreNotMatched(string url)
    {
        var site = new Instagram(_logger);
        var matches = site.Match(url);

        Assert.Empty(matches);
    }

    [Theory]
    [InlineData("https://instagram.com.evil.tld/p/ABC123/")]
    [InlineData("https://cdn.instagram.com/p/ABC123/")]
    [InlineData("https://api.instagram.com/p/ABC123/")]
    [InlineData("https://notinstagram.com/p/ABC123/")]
    public void LookAlikeHostsAreNotMatched(string url)
    {
        var site = new Instagram(_logger);
        var matches = site.Match(url);

        Assert.Empty(matches);
    }

    [Fact]
    public void LinkShimUrlsAreNotMatched()
    {
        // l.instagram.com uses /?u= query parameter, not /p/ or /reel/ paths
        var site = new Instagram(_logger);
        var url = "https://l.instagram.com/?u=https%3A%2F%2Finstagram.com%2Fp%2FABC123%2F";
        var matches = site.Match(url);

        Assert.Empty(matches);
    }

    // Mixed Content - Coexistence

    [Fact]
    public async Task MultipleInstagramUrlsAreAllMatched()
    {
        var site = new Instagram(_logger);
        var content = "Check https://instagram.com/p/ABC123/ and https://instagram.com/reel/EFG456/ out!";
        var matches = site.Match(content);

        Assert.Equal(2, matches.Count);

        var firstResponse = await site.Process(matches[0]);
        var secondResponse = await site.Process(matches[1]);

        Assert.NotNull(firstResponse);
        Assert.Equal("https://d.vxinstagram.com/p/ABC123/", firstResponse.Text);

        Assert.NotNull(secondResponse);
        Assert.Equal("https://d.vxinstagram.com/reel/EFG456/", secondResponse.Text);
    }

    [Fact]
    public void OnlyInstagramUrlsAreMatched()
    {
        // Simulate a message with Instagram and other URLs
        var site = new Instagram(_logger);
        var content = "Check https://instagram.com/p/ABC123/ and https://twitter.com/user/status/123 and https://pixiv.net/artworks/123";
        var matches = site.Match(content);

        // This handler should match only the Instagram URL
        Assert.Single(matches);
        Assert.Contains("instagram.com/p/ABC123", matches[0].Value);
    }
}
