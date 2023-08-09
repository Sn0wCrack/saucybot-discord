using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using SaucyBot.Library.Sites.FurAffinity;
using SaucyBot.Site;
using NSubstitute;
using Xunit;

namespace SaucyBot.Tests.Unit.Site;

public class FurAffinityTest
{
    [Fact]
    public async void SingleEmbedIsReturnWhenTheApiClientReturnsSuccessfullyTest()
    {
        var logger = Substitute.For<ILogger<FurAffinity>>();

        var client = Substitute.For<IFurAffinityClient>();

        var submission = new FaExportSubmission(
            "You're A Furry Aren't Ya? (Nude Alt)",
            "I was going to post this later in the week, but since today is internationalassday. I just had to.",
            "I was going to post this later in the week, but since today is internationalassday. I just had to.",
            "PinkCappachino",
            "https://www.furaffinity.net/user/pinkcappachino/",
            "PinkCappachino",
            "https://a.furaffinity.net/1590197376/pinkcappachino.gif",
            "https://www.furaffinity.net/view/38790081/",
            "Oct 21, 2020 02:07 AM",
            "2020-10-21T02:07:00Z",
            "https://d.furaffinity.net/art/pinkcappachino/1603242451/1603242451.pinkcappachino_goat_post_.png",
            "https://d.furaffinity.net/art/pinkcappachino/1603242451/1603242451.pinkcappachino_goat_post_.png",
            "https://t.furaffinity.net/38790081@400-1603242451.jpg",
            "Artwork (Digital)",
            "Fanart",
            "Goat",
            "Female",
            "383",
            "5",
            "1904",
            "900x552",
            "Adult",
            new[] { "Helltaker", "Lucifier", "Nude", "Alt" }
        );

        client
            .GetSubmission(Arg.Any<string>())
            .Returns(submission);
        
        var site = new FurAffinity(logger, client);

        var match = site.Match("https://www.furaffinity.net/view/38790081/").First();

        var response = await site.Process(match);
        
        Assert.NotNull(response);
        Assert.Single(response.Embeds);

        var embed = response.Embeds.First();
        
        Assert.Equal(submission.Title, embed.Title);
        Assert.Equal(submission.Description, embed.Description);
        Assert.Equal(submission.Link, embed.Url);
        Assert.Equal(submission.Download, embed.Image?.Url);
        Assert.Equal(DateTimeOffset.Parse(submission.PostedAt), embed.Timestamp);
        Assert.Equal(submission.ProfileName, embed.Author?.Name);
        Assert.Equal(submission.Profile, embed.Author?.Url);
        Assert.Equal(submission.Avatar, embed.Author?.IconUrl);
        
        Assert.NotEmpty(embed.Fields);
        
        Assert.Equal("FurAffinity", embed.Footer?.Text);
    }
    
    [Fact]
    public async void NothingIsReturnedWhenTheApiClientReturnsUnsuccessfully()
    {
        var logger = Substitute.For<ILogger<FurAffinity>>();

        var client = Substitute.For<IFurAffinityClient>();

        client
            .GetSubmission(Arg.Any<string>())
            .Returns((FaExportSubmission?) null);

        var site = new FurAffinity(logger, client);

        var match = site.Match("https://www.furaffinity.net/view/38790081/").First();

        var response = await site.Process(match);
        
        Assert.Null(response);
    }
}

