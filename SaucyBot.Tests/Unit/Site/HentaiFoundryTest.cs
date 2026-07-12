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
    [Fact(Skip = "Need to find way to mock HTML parser style classes")]
    public async Task SingleEmbedIsCreatedWhenTheApiClientReturnsSuccessfully()
    {
        // Post: https://www.hentai-foundry.com/pictures/user/cherry-gig/1042457/FOR-THE-GOD-EMPEROR

        var logger = Substitute.For<ILogger<HentaiFoundry>>();

        var client = Substitute.For<IHentaiFoundryClient>();

        var picture = new HentaiFoundryPicture("");

        client
            .GetPage(Arg.Any<string>())
            .Returns((HentaiFoundryPicture?)picture);

        var site = new HentaiFoundry(
            logger,
            client
        );

        var match = site.Pattern.Matches("https://www.hentai-foundry.com/pictures/user/cherry-gig/1042457/FOR-THE-GOD-EMPEROR").First();

        var response = await site.Process(new ProcessRequest(match));

        Assert.NotNull(response);
        Assert.Single(response.Embeds);
    }


    [Fact]
    public async Task NothingIsReturnedWhenTheApiClientReturnsUnsuccessfully()
    {
        // Post: https://www.hentai-foundry.com/pictures/user/cherry-gig/1042457/FOR-THE-GOD-EMPEROR

        var logger = Substitute.For<ILogger<HentaiFoundry>>();

        var client = Substitute.For<IHentaiFoundryClient>();

        client
            .GetPage(Arg.Any<string>())
            .Returns((HentaiFoundryPicture?)null);

        var site = new HentaiFoundry(
            logger,
            client
        );

        var match = site.Pattern.Matches("https://www.hentai-foundry.com/pictures/user/cherry-gig/1042457/FOR-THE-GOD-EMPEROR").First();

        var response = await site.Process(new ProcessRequest(match));

        Assert.Null(response);
    }

    [Fact]
    public void PicturesOnSeparateLinesBeforeAndAfterSameLinePicturesAreAllMatched()
    {
        var logger = Substitute.For<ILogger<HentaiFoundry>>();
        var client = Substitute.For<IHentaiFoundryClient>();

        var site = new HentaiFoundry(logger, client);

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
        var logger = Substitute.For<ILogger<HentaiFoundry>>();
        var client = Substitute.For<IHentaiFoundryClient>();

        var site = new HentaiFoundry(logger, client);

        var content = "pic https://www.hentai-foundry.com/pictures/user/first/1/slug1 also https://www.hentai-foundry.com/pictures/user/second/2/slug2 end";

        var matches = site.Pattern.Matches(content);

        Assert.Equal(2, matches.Count);

        Assert.Equal("first", matches[0].Groups["user"].Value);
        Assert.Equal("1", matches[0].Groups["id"].Value);

        Assert.Equal("second", matches[1].Groups["user"].Value);
        Assert.Equal("2", matches[1].Groups["id"].Value);
    }
}
