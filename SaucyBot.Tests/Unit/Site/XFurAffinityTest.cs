using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SaucyBot.Site;
using SaucyBot.Site.FurAffinity;
using Xunit;

namespace SaucyBot.Tests.Unit.Site;

public class XFurAffinityTest
{
    private readonly ILogger<XFurAffinity> _logger = Substitute.For<ILogger<XFurAffinity>>();

    // Positive Cases - Should Rewrite

    [Theory]
    [InlineData("https://furaffinity.net/view/12345/", "https://xfuraffinity.net/view/12345")]
    [InlineData("https://furaffinity.net/view/12345", "https://xfuraffinity.net/view/12345")]
    [InlineData("https://www.furaffinity.net/view/12345/", "https://xfuraffinity.net/view/12345")]
    public async Task ViewUrlsAreRewrittenCorrectly(string originalUrl, string expectedUrl)
    {
        var site = new XFurAffinity(_logger);
        var match = site.Pattern.Matches(originalUrl).First();
        var response = await site.Process(new ProcessRequest(match));

        Assert.NotNull(response);
        Assert.Equal(expectedUrl, response.Text);
    }

    [Theory]
    [InlineData("https://furaffinity.net/full/12345/", "https://xfuraffinity.net/full/12345")]
    [InlineData("https://furaffinity.net/full/12345", "https://xfuraffinity.net/full/12345")]
    [InlineData("https://www.furaffinity.net/full/12345/", "https://xfuraffinity.net/full/12345")]
    public async Task FullUrlsAreRewrittenCorrectly(string originalUrl, string expectedUrl)
    {
        var site = new XFurAffinity(_logger);
        var match = site.Pattern.Matches(originalUrl).First();
        var response = await site.Process(new ProcessRequest(match));

        Assert.NotNull(response);
        Assert.Equal(expectedUrl, response.Text);
    }

    [Theory]
    [InlineData("https://furaffinity.net/view/12345/?key=value", "https://xfuraffinity.net/view/12345?key=value")]
    [InlineData("https://www.furaffinity.net/full/67890/?foo=bar&baz=qux", "https://xfuraffinity.net/full/67890?foo=bar&baz=qux")]
    public async Task QueryParametersArePreserved(string originalUrl, string expectedUrl)
    {
        var site = new XFurAffinity(_logger);
        var match = site.Pattern.Matches(originalUrl).First();
        var response = await site.Process(new ProcessRequest(match));

        Assert.NotNull(response);
        Assert.Equal(expectedUrl, response.Text);
    }

    [Theory]
    [InlineData("https://furaffinity.net/view/12345/#comments", "https://xfuraffinity.net/view/12345#comments")]
    [InlineData("https://www.furaffinity.net/full/67890/#top", "https://xfuraffinity.net/full/67890#top")]
    public async Task FragmentsArePreserved(string originalUrl, string expectedUrl)
    {
        var site = new XFurAffinity(_logger);
        var match = site.Pattern.Matches(originalUrl).First();
        var response = await site.Process(new ProcessRequest(match));

        Assert.NotNull(response);
        Assert.Equal(expectedUrl, response.Text);
    }

    [Theory]
    [InlineData("https://furaffinity.net/view/12345/?key=value#anchor", "https://xfuraffinity.net/view/12345?key=value#anchor")]
    public async Task QueryAndFragmentAreBothPreserved(string originalUrl, string expectedUrl)
    {
        var site = new XFurAffinity(_logger);
        var match = site.Pattern.Matches(originalUrl).First();
        var response = await site.Process(new ProcessRequest(match));

        Assert.NotNull(response);
        Assert.Equal(expectedUrl, response.Text);
    }

    [Theory]
    [InlineData("http://furaffinity.net/view/12345/", "https://xfuraffinity.net/view/12345")]
    [InlineData("https://furaffinity.net/view/12345/", "https://xfuraffinity.net/view/12345")]
    public async Task BothHttpAndHttpsMatch(string originalUrl, string expectedUrl)
    {
        var site = new XFurAffinity(_logger);
        var match = site.Pattern.Matches(originalUrl).First();
        var response = await site.Process(new ProcessRequest(match));

        Assert.NotNull(response);
        Assert.Equal(expectedUrl, response.Text);
    }

    // Negative Cases - Should Not Match

    [Theory]
    [InlineData("https://xfuraffinity.net/view/12345/")]
    [InlineData("https://xfuraffinity.net/full/12345/")]
    public void XFuraffinityUrlsAreNotMatched(string url)
    {
        var site = new XFurAffinity(_logger);
        var matches = site.Pattern.Matches(url);

        Assert.Empty(matches);
    }

    [Theory]
    [InlineData("https://furaffinity.net/user/username/")]
    [InlineData("https://www.furaffinity.net/gallery/12345/")]
    public void NonSupportedEndpointsAreNotMatched(string url)
    {
        var site = new XFurAffinity(_logger);
        var matches = site.Pattern.Matches(url);

        Assert.Empty(matches);
    }

    // Mixed Content - Coexistence

    [Fact]
    public void OnlyFurAffinityUrlsAreMatched()
    {
        var site = new XFurAffinity(_logger);
        var content = "Check https://furaffinity.net/view/12345/ and https://twitter.com/user/status/123";
        var matches = site.Pattern.Matches(content);

        Assert.Single(matches);
        Assert.Contains("furaffinity.net/view/12345", matches[0].Value);
    }
}
