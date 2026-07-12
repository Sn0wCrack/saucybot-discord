using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SaucyBot.Site;
using SaucyBot.Site.Reddit;
using Xunit;

namespace SaucyBot.Tests.Unit.Site;

public class RedditTest
{
    [Fact]
    public async Task ReturnsDecodedUrlInResponseText()
    {
        var logger = Substitute.For<ILogger<Reddit>>();

        var site = new Reddit(logger);

        var matches = site.Pattern.Matches("https://www.reddit.com/media?url=https%3A%2F%2Fexample.com%2Fimage.jpg");
        var match = matches[0];

        var result = await site.Process(new ProcessRequest(match));

        Assert.NotNull(result);
        Assert.Equal("https://example.com/image.jpg", result.Text);
    }

    [Fact]
    public async Task ReturnsDecodedUrlForComplexEncodedUrl()
    {
        var logger = Substitute.For<ILogger<Reddit>>();

        var site = new Reddit(logger);

        var matches = site.Pattern.Matches("https://reddit.com/media?url=https%3A%2F%2Fwww.youtube.com%2Fwatch%3Fv%3Dabc123");
        var match = matches[0];

        var result = await site.Process(new ProcessRequest(match));

        Assert.NotNull(result);
        Assert.Equal("https://www.youtube.com/watch?v=abc123", result.Text);
    }
}
