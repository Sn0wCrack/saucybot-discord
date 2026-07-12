using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SaucyBot.Library.Sites.Newgrounds;
using SaucyBot.Site;
using Xunit;

namespace SaucyBot.Tests.Unit.Site;

public class NewgroundsTest
{
    [Fact]
    public async Task AnEmbedIsCreatedForArtPost()
    {
        var logger = Substitute.For<ILogger<Newgrounds>>();

        var client = Substitute.For<INewgroundsClient>();

        var html = @"
            <html>
                <div class='body-guts'>
                    <div class='column wide right'>
                        <div class='pod-head'><h2>Test Art Title</h2></div>
                    </div>
                </div>
                <div id='author_comments'>Test description</div>
                <div class='pod-body'>
                    <div class='image'><img src='https://example.com/art.jpg' /></div>
                </div>
                <div class='sidestats'>
                    <dt>Views</dt><dd>1000</dd>
                </div>
                <div id='score_number'>4.5</div>
            </html>";

        var art = new NewgroundsArt(html);

        client
            .GetArt(Arg.Any<string>(), Arg.Any<string>())
            .Returns(art);

        var site = new Newgrounds(logger, client);

        var matches = site.Match("https://www.newgrounds.com/art/view/testuser/test-slug");
        var match = matches[0];

        var result = await site.Process(new ProcessRequest(match));

        Assert.NotNull(result);
        Assert.NotEmpty(result.Embeds);
        Assert.Single(result.Embeds);
    }

    [Fact]
    public async Task NothingIsReturnedWhenTheApiClientReturnsUnsuccessfully()
    {
        var logger = Substitute.For<ILogger<Newgrounds>>();

        var client = Substitute.For<INewgroundsClient>();

        client
            .GetArt(Arg.Any<string>(), Arg.Any<string>())
            .Returns((NewgroundsArt?)null);

        var site = new Newgrounds(logger, client);

        var matches = site.Match("https://www.newgrounds.com/art/view/testuser/test-slug");
        var match = matches[0];

        var result = await site.Process(new ProcessRequest(match));

        Assert.Null(result);
    }

    [Fact]
    public void ArtPostsOnSeparateLinesBeforeAndAfterSameLinePostsAreAllMatched()
    {
        var logger = Substitute.For<ILogger<Newgrounds>>();
        var client = Substitute.For<INewgroundsClient>();

        var site = new Newgrounds(logger, client);

        var content =
            "https://www.newgrounds.com/art/view/first/slug1\n" +
            "https://www.newgrounds.com/art/view/second/slug2 https://www.newgrounds.com/art/view/third/slug3\n" +
            "https://www.newgrounds.com/art/view/fourth/slug4";

        var matches = site.Match(content);

        Assert.Equal(4, matches.Count);

        Assert.Equal("first", matches[0].Groups["user"].Value);
        Assert.Equal("slug1", matches[0].Groups["slug"].Value);

        Assert.Equal("second", matches[1].Groups["user"].Value);
        Assert.Equal("slug2", matches[1].Groups["slug"].Value);

        Assert.Equal("third", matches[2].Groups["user"].Value);
        Assert.Equal("slug3", matches[2].Groups["slug"].Value);

        Assert.Equal("fourth", matches[3].Groups["user"].Value);
        Assert.Equal("slug4", matches[3].Groups["slug"].Value);
    }

    [Fact]
    public void ArtPostsSurroundedByTextOnASingleLineAreAllMatched()
    {
        var logger = Substitute.For<ILogger<Newgrounds>>();
        var client = Substitute.For<INewgroundsClient>();

        var site = new Newgrounds(logger, client);

        var content = "art https://www.newgrounds.com/art/view/first/slug1 plus https://www.newgrounds.com/art/view/second/slug2 done";

        var matches = site.Match(content);

        Assert.Equal(2, matches.Count);

        Assert.Equal("first", matches[0].Groups["user"].Value);
        Assert.Equal("slug1", matches[0].Groups["slug"].Value);

        Assert.Equal("second", matches[1].Groups["user"].Value);
        Assert.Equal("slug2", matches[1].Groups["slug"].Value);
    }
}
