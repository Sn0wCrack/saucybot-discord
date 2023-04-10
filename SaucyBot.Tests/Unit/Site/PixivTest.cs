using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
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
        // Post: https://www.pixiv.net/en/artworks/106848609
        
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
            new(
                new IllustrationPagesUrls(
                    "https://i.pximg.net/c/128x128/img-master/img/2023/04/04/06/00/11/106848609_p0_square1200.jpg", 
                    "https://i.pximg.net/c/540x540_70/img-master/img/2023/04/04/06/00/11/106848609_p0_master1200.jpg", 
                    "https://i.pximg.net/img-master/img/2023/04/04/06/00/11/106848609_p0_master1200.jpg", 
                    "https://i.pximg.net/img-original/img/2023/04/04/06/00/11/106848609_p0.jpg"
                ),
                1296,
                2366
            ),
            new(
                new IllustrationPagesUrls(
                    "https://i.pximg.net/c/128x128/img-master/img/2023/04/04/06/00/11/106848609_p1_square1200.jpg", 
                    "https://i.pximg.net/c/540x540_70/img-master/img/2023/04/04/06/00/11/106848609_p1_master1200.jpg", 
                    "https://i.pximg.net/img-master/img/2023/04/04/06/00/11/106848609_p1_master1200.jpg", 
                    "https://i.pximg.net/img-original/img/2023/04/04/06/00/11/106848609_p1.jpg"
                ),
                1296,
                2366
            ),
            new(
                new IllustrationPagesUrls(
                    "https://i.pximg.net/c/128x128/img-master/img/2023/04/04/06/00/11/106848609_p2_square1200.jpg", 
                    "https://i.pximg.net/c/540x540_70/img-master/img/2023/04/04/06/00/11/106848609_p2_master1200.jpg", 
                    "https://i.pximg.net/img-master/img/2023/04/04/06/00/11/106848609_p2_master1200.jpg", 
                    "https://i.pximg.net/img-original/img/2023/04/04/06/00/11/106848609_p2.jpg"
                ),
                972,
                1775
            ),
            new(
                new IllustrationPagesUrls(
                    "https://i.pximg.net/c/128x128/img-master/img/2023/04/04/06/00/11/106848609_p3_square1200.jpg", 
                    "https://i.pximg.net/c/540x540_70/img-master/img/2023/04/04/06/00/11/106848609_p3_master1200.jpg", 
                    "https://i.pximg.net/img-master/img/2023/04/04/06/00/11/106848609_p3_master1200.jpg", 
                    "https://i.pximg.net/img-original/img/2023/04/04/06/00/11/106848609_p3.jpg"
                ),
                1134,
                2037
            ),
        };

        var illustrationPagesResponse = new IllustrationPagesResponse(
            false,
            "Test Response",
            illustrationPages
        );
        
        client.Setup(mock => mock.Login())
            .ReturnsAsync(true);

        client.Setup(mock => mock.IllustrationDetails(It.IsAny<string>()))
            .ReturnsAsync((IllustrationDetailsResponse?) illustrationDetailsResponse);

        client.Setup(mock => mock.IllustrationPages(It.IsAny<string>()))
            .ReturnsAsync((IllustrationPagesResponse?) illustrationPagesResponse);
        
        client.Setup(mock => mock.PokeFile(It.IsAny<string>()))
            .ReturnsAsync(new HttpResponseMessage());

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
        
        Assert.NotNull(response);
        Assert.NotEmpty(response.Files);
        Assert.Equal(4, response.Files.Count);
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