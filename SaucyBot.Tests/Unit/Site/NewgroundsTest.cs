using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SaucyBot.Library.Sites.Newgrounds;
using SaucyBot.Site;
using NSubstitute;
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

        var result = await site.Process(match);
        
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

        var result = await site.Process(match);
        
        Assert.Null(result);
    }
}
