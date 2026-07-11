using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SaucyBot.Library.Sites.E621;
using SaucyBot.Site;
using NSubstitute;
using Xunit;

namespace SaucyBot.Tests.Unit.Site;

public class E621Test
{
    [Fact]
    public async Task SingleEmbedIsReturnedWhenTheApiClientReturnsSuccessfully()
    {
        var logger = Substitute.For<ILogger<E621>>();
        var client = Substitute.For<IE621Client>();

        var post = new E621PostResponse(
            new E621Post(
                12345,
                "2024-01-01T00:00:00.000-00:00",
                "2024-01-01T00:00:00.000-00:00",
                new E621PostFile(800, 600, "jpg", 102400, "abc123", "https://example.com/image.jpg"),
                new E621PostPreview(150, 112, "https://example.com/preview.jpg"),
                new E621PostSample(true, 800, 600, "https://example.com/sample.jpg"),
                new E621PostScore(100, 5, 95),
                new E621PostTags(new[] { "artist1", "artist2" }, new[] { "animated" }),
                "Test description"
            )
        );

        client
            .GetPost(Arg.Any<string>())
            .Returns(post);

        var site = new E621(logger, client);

        var matches = site.Match("https://e621.net/posts/12345");
        var match = matches[0];

        var result = await site.Process(new ProcessRequest(match));

        Assert.NotNull(result);
        Assert.NotEmpty(result.Embeds);
        Assert.Single(result.Embeds);
        Assert.Contains("[ANIM]", result.Embeds[0].Title);
    }

    [Fact]
    public async Task NothingIsReturnedWhenTheApiClientReturnsUnsuccessfully()
    {
        var logger = Substitute.For<ILogger<E621>>();
        var client = Substitute.For<IE621Client>();

        client
            .GetPost(Arg.Any<string>())
            .Returns((E621PostResponse?)null);

        var site = new E621(logger, client);

        var matches = site.Match("https://e621.net/posts/12345");
        var match = matches[0];

        var result = await site.Process(new ProcessRequest(match));

        Assert.Null(result);
    }

    [Fact]
    public async Task EmbedIsReturnedWithoutAnimTag()
    {
        var logger = Substitute.For<ILogger<E621>>();
        var client = Substitute.For<IE621Client>();

        var post = new E621PostResponse(
            new E621Post(
                12346,
                "2024-01-01T00:00:00.000-00:00",
                "2024-01-01T00:00:00.000-00:00",
                new E621PostFile(800, 600, "jpg", 102400, "abc123", "https://example.com/image.jpg"),
                new E621PostPreview(150, 112, "https://example.com/preview.jpg"),
                new E621PostSample(true, 800, 600, "https://example.com/sample.jpg"),
                new E621PostScore(100, 5, 95),
                new E621PostTags(new[] { "artist1" }, []),
                "Test description"
            )
        );

        client
            .GetPost(Arg.Any<string>())
            .Returns(post);

        var site = new E621(logger, client);

        var matches = site.Match("https://e621.net/posts/12346");
        var match = matches[0];

        var result = await site.Process(new ProcessRequest(match));

        Assert.NotNull(result);
        Assert.NotEmpty(result.Embeds);
        Assert.Single(result.Embeds);
        Assert.DoesNotContain("[ANIM]", result.Embeds[0].Title);
    }
}
