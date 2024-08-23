using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SaucyBot.Library.Sites.ArtStation;
using SaucyBot.Site;
using Xunit;

namespace SaucyBot.Tests.Unit.Site;

public class ArtStationTest
{
    [Fact]
    public async Task AnEmbedIsCreatedForEachProjectImageAsset()
    {
        var logger = Substitute.For<ILogger<ArtStation>>();
        
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                {"Sites:ArtStation:PostLimit", "8"}
            })
            .Build();
        
        var client = Substitute.For<IArtStationClient>();
        
        var user = new ProjectUser(
            "zhongguoduliu",
            "毒瘤",
            "https://www.artstation.com/zhongguoduliu",
            "https://cdna.artstation.com/p/users/avatars/000/510/818/large/98e5fe8491e9a55928fc3a6495118124.jpg?1562744573",
            "https://cdna.artstation.com/p/users/avatars/000/510/818/medium/98e5fe8491e9a55928fc3a6495118124.jpg?1562744573",
            "https://cdna.artstation.com/p/users/covers/000/510/818/small/299240604666715b8b319b6aa2195d3f.jpg?1573815320"
        );

        var assets = new List<ProjectAsset>
        {
            new(
                54248959,
                null,
                "cover",
                "https://cdnb.artstation.com/p/assets/covers/images/054/248/959/large/-wwdefef.jpg?1664115891",
                1779,
                1779,
                0,
                false,
                false
            ),
            new(
                54249518,
                null,
                "image",
                "https://cdna.artstation.com/p/assets/images/images/054/249/518/large/-wfwffsawdef.jpg?1664116869",
                5397,
                7836,
                0,
                true,
                false
            ),
            new(
                54249430,
                null,
                "image",
                "https://cdna.artstation.com/p/assets/images/images/054/249/430/large/-wfwffsawdef.jpg?1664116708",
                5397,
                8136,
                1,
                true,
                false
            ),
            new(
                54360326,
                null,
                "image",
                "https://cdna.artstation.com/p/assets/images/images/054/360/326/large/-2331212.jpg?1664363138",
                638,
                676,
                3,
                true,
                false
            ),
        };

        var project = new Project(
            13877099,
            "黑发女子抱着火烈鸟游泳圈     ",
            "<p>手里的冰淇淋都是爱你的形状</p>",
            "https://cdnb.artstation.com/p/assets/covers/images/054/248/959/medium/-wwdefef.jpg?1664115891",
            "https://www.artstation.com/artwork/8wX69G",
            387,
            2412,
            "2022-09-28T00:49:29.392-05:00",
            user,
            assets
        );

        client
            .GetProject(Arg.Any<string>())
            .Returns(project);

        var site = new ArtStation(logger, config, client);

        var match = site.Match("https://www.artstation.com/artwork/xYXO5X").First();

        var response = await site.Process(match);
        
        Assert.NotNull(response);
        Assert.NotEmpty(response.Embeds);
        Assert.Equal(4, response.Embeds.Count);
    }

    [Fact]
    public async Task NothingIsReturnedWhenTheApiClientReturnsUnsuccessfully()
    {
        var logger = Substitute.For<ILogger<ArtStation>>();
        
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                {"Sites:ArtStation:PostLimit", "8"}
            })
            .Build();
        
        var client = Substitute.For<IArtStationClient>();;

        client
            .GetProject(Arg.Any<string>())
            .Returns((Project?) null);

        var site = new ArtStation(logger, config, client);

        var match = site.Match("https://www.artstation.com/artwork/xYXO5X").First();

        var response = await site.Process(match);
        
        Assert.Null(response);
    }
}