using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using SaucyBot.Library.Sites.Pixiv;
using SaucyBot.Services;
using SaucyBot.Site;
using Xunit;

namespace SaucyBot.Tests.Unit.Site;

public class PixivTest
{
    [Fact]
    public async void AFileIsCreatedForEachImageWithinMultiImagePost()
    {
        var logger = Mock.Of<ILogger<Pixiv>>();
        
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                {"Sites:Pixiv:PostLimit", "5"}
            })
            .Build();
        
        var guildConfigurationManager = new Mock<IGuildConfigurationManager>();
        
        var client = new Mock<IPixivClient>();

        var illustrationDetails = new IllustrationDetails(
            "106848609",
            "vs鬼",
            "",
            IllustrationType.Illustration,
            new IllustrationDetailsUrls("", "", "", "", ""),
            4
        );
        
        var illustrationDetailsResponse = new IllustrationDetailsResponse(
            false,
            "Test Response",
            illustrationDetails
        );

        var illustrationPages = new List<IllustrationPages>
        {
            new IllustrationPages(
                new IllustrationPagesUrls("", "", "", ""),
                0,
                0
            ),
            new IllustrationPages(
                new IllustrationPagesUrls("", "", "", ""),
                0,
                0
            ),
            new IllustrationPages(
                new IllustrationPagesUrls("", "", "", ""),
                0,
                0
            ),
            new IllustrationPages(
                new IllustrationPagesUrls("", "", "", ""),
                0,
                0
            ),
        };

        var illustrationPagesResponse = new IllustrationPagesResponse(
            false,
            "Test Response",
            illustrationPages
        );

        client.Setup(mock => mock.IllustrationDetails(It.IsAny<string>()))
            .ReturnsAsync((IllustrationDetailsResponse?) illustrationDetailsResponse);

        client.Setup(mock => mock.IllustrationPages(It.IsAny<string>()))
            .ReturnsAsync((IllustrationPagesResponse?) illustrationPagesResponse);

        client.Setup(mock => mock.GetFile(It.IsAny<string>()))
            .ReturnsAsync(new MemoryStream());

        var site = new Pixiv(
            logger,
            config,
            guildConfigurationManager.Object,
            client.Object
        );

        var match = site.Match("https://www.pixiv.net/en/artworks/106848609").First();

        var response = await site.Process(match);
        
        Assert.Null(response);
    }
    
    [Fact]
    public async void NothingIsReturnedWhenTheApiClientReturnsUnsuccessfully()
    {
        var logger = Mock.Of<ILogger<Pixiv>>();
        
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                {"Sites:Pixiv:PostLimit", "5"}
            })
            .Build();
        
        var guildConfigurationManager = new Mock<IGuildConfigurationManager>();
        
        var client = new Mock<IPixivClient>();

        client.Setup(mock => mock.IllustrationDetails(It.IsAny<string>()))
            .ReturnsAsync((IllustrationDetailsResponse?) null);

        var site = new Pixiv(
            logger,
            config,
            guildConfigurationManager.Object,
            client.Object
        );

        var match = site.Match("https://www.pixiv.net/en/artworks/79124301").First();

        var response = await site.Process(match);
        
        Assert.Null(response);
    }
}