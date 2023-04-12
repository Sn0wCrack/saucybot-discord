using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Moq;
using SaucyBot.Library.Sites.DeviantArt;
using SaucyBot.Site;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace SaucyBot.Tests.Unit.Site;

public class DeviantArtTest
{
    [Fact]
    public async void SingleEmbedIsReturnedWhenTheApiClientReturnsSuccessfully()
    {
        // Post: https://www.deviantart.com/shadeofshinon/art/Frostbreath-VI-943346591

        var logger = Mock.Of<ILogger<DeviantArt>>();

        var config = new ConfigurationBuilder()
            .Build();
        
        var client = new Mock<IDeviantArtClient>();
        var oembedClient = new Mock<IDeviantArtOpenEmbedClient>();

        var openEmbedResponse = new OpenEmbedResponse(
            "1.0",
            "photo",
            "Frostbreath VI",
            "https://images-wixmp-ed30a86b8c4ca887773594c2.wixmp.com/f/76098ac8-04ab-4784-b382-88ca082ba9b1/dfln6vz-82332f76-8119-4c2a-af75-ae585a623056.jpg?token=eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJ1cm46YXBwOjdlMGQxODg5ODIyNjQzNzNhNWYwZDQxNWVhMGQyNmUwIiwiaXNzIjoidXJuOmFwcDo3ZTBkMTg4OTgyMjY0MzczYTVmMGQ0MTVlYTBkMjZlMCIsIm9iaiI6W1t7InBhdGgiOiJcL2ZcLzc2MDk4YWM4LTA0YWItNDc4NC1iMzgyLTg4Y2EwODJiYTliMVwvZGZsbjZ2ei04MjMzMmY3Ni04MTE5LTRjMmEtYWY3NS1hZTU4NWE2MjMwNTYuanBnIn1dXSwiYXVkIjpbInVybjpzZXJ2aWNlOmZpbGUuZG93bmxvYWQiXX0.cHapSiYBRE3YlOaQzU12B70MYBvMwFQUnnq6e2eeIyo",
            "ShadeOfShinon",
            "https://www.deviantart.com/shadeofshinon",
            "DeviantArt",
            "https://www.deviantart.com",
            "https://images-wixmp-ed30a86b8c4ca887773594c2.wixmp.com/f/76098ac8-04ab-4784-b382-88ca082ba9b1/dfln6vz-82332f76-8119-4c2a-af75-ae585a623056.jpg/v1/fit/w_300,h_707,q_70,strp/frostbreath_vi_by_shadeofshinon_dfln6vz-300w.jpg?token=eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJ1cm46YXBwOjdlMGQxODg5ODIyNjQzNzNhNWYwZDQxNWVhMGQyNmUwIiwiaXNzIjoidXJuOmFwcDo3ZTBkMTg4OTgyMjY0MzczYTVmMGQ0MTVlYTBkMjZlMCIsIm9iaiI6W1t7ImhlaWdodCI6Ijw9NzA3IiwicGF0aCI6IlwvZlwvNzYwOThhYzgtMDRhYi00Nzg0LWIzODItODhjYTA4MmJhOWIxXC9kZmxuNnZ6LTgyMzMyZjc2LTgxMTktNGMyYS1hZjc1LWFlNTg1YTYyMzA1Ni5qcGciLCJ3aWR0aCI6Ijw9MTAwMCJ9XV0sImF1ZCI6WyJ1cm46c2VydmljZTppbWFnZS5vcGVyYXRpb25zIl19.nOe99K1ZJdJSDakOZWL5iGD9Bl60YRgIRqsooYPsCE4"
        );
        
        oembedClient.Setup(mock => mock.Get(It.IsAny<string>()))
            .ReturnsAsync(openEmbedResponse);

        var site = new DeviantArt(
            logger,
            config,
            client.Object,
            oembedClient.Object
        );

        var match = site.Match("https://www.deviantart.com/shadeofshinon/art/Frostbreath-VI-943346591").First();

        var response = await site.Process(match);
        
        Assert.NotNull(response);
        Assert.Single(response.Embeds);

        var embed = response.Embeds.First();
        
        Assert.Equal(openEmbedResponse.Title, embed.Title);
        Assert.Equal(openEmbedResponse.Url, embed.Image?.Url);
        Assert.Equal(openEmbedResponse.AuthorName, embed.Author?.Name);
        Assert.Equal(openEmbedResponse.AuthorUrl, embed.Author?.Url);
        
        Assert.Equal("DeviantArt", embed.Footer?.Text);
    }
    
    [Fact]
    public async void NothingIsReturnedWhenTheApiClientReturnsUnsuccessfully()
    {
        // Post: https://www.deviantart.com/shadeofshinon/art/Frostbreath-VI-943346591

        var logger = Mock.Of<ILogger<DeviantArt>>();

        var config = new ConfigurationBuilder()
            .Build();
        
        var client = new Mock<IDeviantArtClient>();
        var oembedClient = new Mock<IDeviantArtOpenEmbedClient>();
        
        oembedClient.Setup(mock => mock.Get(It.IsAny<string>()))
            .ReturnsAsync((OpenEmbedResponse?) null);

        var site = new DeviantArt(
            logger,
            config,
            client.Object,
            oembedClient.Object
        );

        var match = site.Match("https://www.deviantart.com/shadeofshinon/art/Frostbreath-VI-943346591").First();

        var response = await site.Process(match);
        
        Assert.Null(response);
    }
}