using System.Linq;
using Microsoft.Extensions.Logging;
using SaucyBot.Library.Sites.HentaiFoundry;
using SaucyBot.Site;
using NSubstitute;
using Xunit;

namespace SaucyBot.Tests.Unit.Site;

public class HentaiFoundryTest
{
    [Fact(Skip = "Need to find way to mock HTML parser style classes")]
    public async void SingleEmbedIsCreatedWhenTheApiClientReturnsSuccessfully()
    {
        // Post: https://www.hentai-foundry.com/pictures/user/cherry-gig/1042457/FOR-THE-GOD-EMPEROR
        
        var logger = Substitute.For<ILogger<HentaiFoundry>>();

        var client = Substitute.For<IHentaiFoundryClient>();

        var picture = new HentaiFoundryPicture("");

        client
            .GetPage(Arg.Any<string>())
            .Returns((HentaiFoundryPicture?) picture);

        var site = new HentaiFoundry(
            logger,
            client
        );

        var match = site.Match("https://www.hentai-foundry.com/pictures/user/cherry-gig/1042457/FOR-THE-GOD-EMPEROR").First();

        var response = await site.Process(match);
        
        Assert.NotNull(response);
        Assert.Single(response.Embeds);
    }
    
    
    [Fact]
    public async void NothingIsReturnedWhenTheApiClientReturnsUnsuccessfully()
    {
        // Post: https://www.hentai-foundry.com/pictures/user/cherry-gig/1042457/FOR-THE-GOD-EMPEROR
        
        var logger = Substitute.For<ILogger<HentaiFoundry>>();

        var client = Substitute.For<IHentaiFoundryClient>();

        client
            .GetPage(Arg.Any<string>())
            .Returns((HentaiFoundryPicture?) null);

        var site = new HentaiFoundry(
            logger,
            client
        );

        var match = site.Match("https://www.hentai-foundry.com/pictures/user/cherry-gig/1042457/FOR-THE-GOD-EMPEROR").First();

        var response = await site.Process(match);
        
        Assert.Null(response);
    }
}