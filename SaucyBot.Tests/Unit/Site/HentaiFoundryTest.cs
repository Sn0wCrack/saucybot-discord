using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SaucyBot.Library.Sites.HentaiFoundry;
using SaucyBot.Site;
using SaucyBot.Site.HentaiFoundry;
using Xunit;

namespace SaucyBot.Tests.Unit.Site;

public class HentaiFoundryTest
{
    private const string SamplePageHtml = @"
        <html>
        <body>
            <div class='imageTitle'>Test Art Title</div>
            <div class='picDescript'>This is a test description</div>
            <div id='picBox'>
                <div class='boxbody'>
                    <img src='//pictures/user/cherry-gig/1042457/sample.jpg' />
                </div>
            </div>
            <div id='descriptionBox'>
                <div class='boxbody'>
                    <a href='/pictures/user/cherry-gig'>
                        <img src='//pictures/user/cherry-gig/avatar.png' title='cherry-gig' />
                    </a>
                </div>
            </div>
            <div id='pictureGeneralInfoBox'>
                <div class='boxbody'>
                    <div class='column'>
                        <time datetime='2024-01-15T10:30:00+00:00'>Jan 15, 2024</time>
                    </div>
                    <div class='column'><span>Views</span><span>1234</span></div>
                    <div class='column'><span>Vote Score</span><span>42</span></div>
                </div>
            </div>
        </body>
        </html>";

    [Fact]
    public async Task SingleEmbedIsCreatedWhenTheApiClientReturnsSuccessfully()
    {
        var logger = Substitute.For<ILogger<HentaiFoundrySite>>();
        var client = Substitute.For<IHentaiFoundryClient>();

        client.Agree().Returns(true);

        var picture = new HentaiFoundryPicture(SamplePageHtml);

        client
            .GetPage(Arg.Any<string>())
            .Returns((HentaiFoundryPicture?)picture);

        var site = new HentaiFoundrySite(logger, client);

        var match = site.Pattern.Matches("https://www.hentai-foundry.com/pictures/user/cherry-gig/1042457/FOR-THE-GOD-EMPEROR").First();

        var response = await site.Process(new ProcessRequest(match));

        Assert.NotNull(response);
        Assert.Single(response.Embeds);

        var embed = response.Embeds[0];

        Assert.Equal("Test Art Title", embed.Title);
        Assert.Equal("This is a test description", embed.Description);
        Assert.Equal("https://www.hentai-foundry.com/pictures/user/cherry-gig/1042457/FOR-THE-GOD-EMPEROR", embed.Url);
        Assert.Equal("https://pictures/user/cherry-gig/1042457/sample.jpg", embed.Image?.Url);
        Assert.Equal(new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero), embed.Timestamp);
        Assert.Equal("cherry-gig", embed.Author?.Name);
        Assert.Equal("https://www.hentai-foundry.com/pictures/user/cherry-gig", embed.Author?.Url);
        Assert.Equal("https://pictures/user/cherry-gig/avatar.png", embed.Author?.IconUrl);
    }

    [Fact]
    public async Task NothingIsReturnedWhenTheApiClientReturnsUnsuccessfully()
    {
        var logger = Substitute.For<ILogger<HentaiFoundrySite>>();
        var client = Substitute.For<IHentaiFoundryClient>();

        client.Agree().Returns(true);

        client
            .GetPage(Arg.Any<string>())
            .Returns((HentaiFoundryPicture?)null);

        var site = new HentaiFoundrySite(logger, client);

        var match = site.Pattern.Matches("https://www.hentai-foundry.com/pictures/user/cherry-gig/1042457/FOR-THE-GOD-EMPEROR").First();

        var response = await site.Process(new ProcessRequest(match));

        Assert.Null(response);
    }

    [Fact]
    public async Task NothingIsReturnedWhenTheAgreeCheckFails()
    {
        var logger = Substitute.For<ILogger<HentaiFoundrySite>>();
        var client = Substitute.For<IHentaiFoundryClient>();

        client.Agree().Returns(false);

        var site = new HentaiFoundrySite(logger, client);

        var match = site.Pattern.Matches("https://www.hentai-foundry.com/pictures/user/cherry-gig/1042457/FOR-THE-GOD-EMPEROR").First();

        var response = await site.Process(new ProcessRequest(match));

        Assert.Null(response);
    }

    [Fact]
    public void PicturesOnSeparateLinesBeforeAndAfterSameLinePicturesAreAllMatched()
    {
        var logger = Substitute.For<ILogger<HentaiFoundrySite>>();
        var client = Substitute.For<IHentaiFoundryClient>();

        var site = new HentaiFoundrySite(logger, client);

        var content =
            "https://www.hentai-foundry.com/pictures/user/first/1/slug1\n" +
            "https://www.hentai-foundry.com/pictures/user/second/2/slug2 https://www.hentai-foundry.com/pictures/user/third/3/slug3\n" +
            "https://www.hentai-foundry.com/pictures/user/fourth/4/slug4";

        var matches = site.Pattern.Matches(content);

        Assert.Equal(4, matches.Count);

        Assert.Equal("first", matches[0].Groups["user"].Value);
        Assert.Equal("1", matches[0].Groups["id"].Value);

        Assert.Equal("second", matches[1].Groups["user"].Value);
        Assert.Equal("2", matches[1].Groups["id"].Value);

        Assert.Equal("third", matches[2].Groups["user"].Value);
        Assert.Equal("3", matches[2].Groups["id"].Value);

        Assert.Equal("fourth", matches[3].Groups["user"].Value);
        Assert.Equal("4", matches[3].Groups["id"].Value);
    }

    [Fact]
    public void PicturesSurroundedByTextOnASingleLineAreAllMatched()
    {
        var logger = Substitute.For<ILogger<HentaiFoundrySite>>();
        var client = Substitute.For<IHentaiFoundryClient>();

        var site = new HentaiFoundrySite(logger, client);

        var content = "pic https://www.hentai-foundry.com/pictures/user/first/1/slug1 also https://www.hentai-foundry.com/pictures/user/second/2/slug2 end";

        var matches = site.Pattern.Matches(content);

        Assert.Equal(2, matches.Count);

        Assert.Equal("first", matches[0].Groups["user"].Value);
        Assert.Equal("1", matches[0].Groups["id"].Value);

        Assert.Equal("second", matches[1].Groups["user"].Value);
        Assert.Equal("2", matches[1].Groups["id"].Value);
    }
}
