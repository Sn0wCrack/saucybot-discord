using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using SaucyBot.Library.Sites.HentaiFoundry;
using SaucyBot.Services;
using SaucyBot.Site;
using Xunit;

namespace SaucyBot.Tests.Unit.Site;

public class HentaiFoundryTest
{
    [Fact]
    public async void SingleEmbedIsCreatedWhenTheApiClientReturnsSuccessfully()
    {
        // Post: https://www.hentai-foundry.com/pictures/user/cherry-gig/1042457/FOR-THE-GOD-EMPEROR
        
        var logger = Mock.Of<ILogger<HentaiFoundry>>();

        var client = new Mock<IHentaiFoundryClient>();

        var picture = new HentaiFoundryPicture("");

        client.Setup(mock => mock.GetPage(It.IsAny<string>()))
            .ReturnsAsync((HentaiFoundryPicture?) picture);

        var site = new HentaiFoundry(
            logger,
            client.Object
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
        
        var logger = Mock.Of<ILogger<HentaiFoundry>>();

        var client = new Mock<IHentaiFoundryClient>();
        
        client.Setup(mock => mock.GetPage(It.IsAny<string>()))
            .ReturnsAsync((HentaiFoundryPicture?) null);

        var site = new HentaiFoundry(
            logger,
            client.Object
        );

        var match = site.Match("https://www.hentai-foundry.com/pictures/user/cherry-gig/1042457/FOR-THE-GOD-EMPEROR").First();

        var response = await site.Process(match);
        
        Assert.Null(response);
    }
}