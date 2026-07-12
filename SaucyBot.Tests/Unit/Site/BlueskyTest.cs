using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SaucyBot.Library.Sites.BlueSky;
using SaucyBot.Site;
using Xunit;

namespace SaucyBot.Tests.Unit.Site;

public class BlueskyTest
{
    [Fact]
    public async Task AnEmbedIsCreatedForEachImageInPost()
    {
        var logger = Substitute.For<ILogger<Bluesky>>();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                {"Sites:Bluesky:Delay", "3"}
            })
            .Build();

        var client = Substitute.For<IVixBlueskyClient>();

        var post = new VixBlueskyPost(
            new VixBlueskyUser("testuser", "Test User", "https://example.com/avatar.jpg"),
            new VixBlueskyRecord("app.bsky.feed.post", "2024-01-01T00:00:00Z", "Test post content", null),
            new VixBlueskyEmbed("app.bsky.embed.images#view", null, new List<VixBlueskyEmbedImage>
            {
                new("https://example.com/thumb1.jpg", "https://example.com/image1.jpg"),
                new("https://example.com/thumb2.jpg", "https://example.com/image2.jpg"),
            }, null, null),
            null,
            5,
            10,
            20,
            3
        );

        var response = new VixBlueskyResponse(new List<VixBlueskyPost> { post });

        client
            .GetPost(Arg.Any<string>(), Arg.Any<string>())
            .Returns(response);

        var site = new Bluesky(logger, config, client);

        var match = site.Match("https://bsky.app/profile/testuser/post/3kabc123").First();

        var result = await site.Process(new ProcessRequest(match));

        Assert.NotNull(result);
        Assert.NotEmpty(result.Embeds);
        Assert.Equal(2, result.Embeds.Count);
    }

    [Fact]
    public async Task NothingIsReturnedWhenTheApiClientReturnsUnsuccessfully()
    {
        var logger = Substitute.For<ILogger<Bluesky>>();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                {"Sites:Bluesky:Delay", "3"}
            })
            .Build();

        var client = Substitute.For<IVixBlueskyClient>();

        client
            .GetPost(Arg.Any<string>(), Arg.Any<string>())
            .Returns((VixBlueskyResponse?)null);

        var site = new Bluesky(logger, config, client);

        var match = site.Match("https://bsky.app/profile/testuser/post/3kabc123").First();

        var result = await site.Process(new ProcessRequest(match));

        Assert.Null(result);
    }

    [Fact]
    public async Task HandlesPostWithNoImages()
    {
        var logger = Substitute.For<ILogger<Bluesky>>();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                {"Sites:Bluesky:Delay", "3"}
            })
            .Build();

        var client = Substitute.For<IVixBlueskyClient>();

        var post = new VixBlueskyPost(
            new VixBlueskyUser("testuser", "Test User", "https://example.com/avatar.jpg"),
            new VixBlueskyRecord("app.bsky.feed.post", "2024-01-01T00:00:00Z", "Test post content", null),
            null,
            null,
            5,
            10,
            20,
            3
        );

        var response = new VixBlueskyResponse(new List<VixBlueskyPost> { post });

        client
            .GetPost(Arg.Any<string>(), Arg.Any<string>())
            .Returns(response);

        var site = new Bluesky(logger, config, client);

        var match = site.Match("https://bsky.app/profile/testuser/post/3kabc123").First();

        var result = await site.Process(new ProcessRequest(match));

        Assert.NotNull(result);
        Assert.NotEmpty(result.Embeds);
        Assert.Single(result.Embeds);
    }

    [Fact]
    public void PostsOnSeparateLinesBeforeAndAfterSameLinePostsAreAllMatched()
    {
        var logger = Substitute.For<ILogger<Bluesky>>();
        var config = new ConfigurationBuilder().Build();
        var client = Substitute.For<IVixBlueskyClient>();

        var site = new Bluesky(logger, config, client);

        var content =
            "https://bsky.app/profile/first.bsky.social/post/p1\n" +
            "https://bsky.app/profile/second.bsky.social/post/p2 https://bsky.app/profile/third.bsky.social/post/p3\n" +
            "https://bsky.app/profile/fourth.bsky.social/post/p4";

        var matches = site.Match(content);

        Assert.Equal(4, matches.Count);

        Assert.Equal("first.bsky.social", matches[0].Groups["user"].Value);
        Assert.Equal("p1", matches[0].Groups["id"].Value);

        Assert.Equal("second.bsky.social", matches[1].Groups["user"].Value);
        Assert.Equal("p2", matches[1].Groups["id"].Value);

        Assert.Equal("third.bsky.social", matches[2].Groups["user"].Value);
        Assert.Equal("p3", matches[2].Groups["id"].Value);

        Assert.Equal("fourth.bsky.social", matches[3].Groups["user"].Value);
        Assert.Equal("p4", matches[3].Groups["id"].Value);
    }

    [Fact]
    public void PostsSurroundedByTextOnASingleLineAreAllMatched()
    {
        var logger = Substitute.For<ILogger<Bluesky>>();
        var config = new ConfigurationBuilder().Build();
        var client = Substitute.For<IVixBlueskyClient>();

        var site = new Bluesky(logger, config, client);

        var content = "look https://bsky.app/profile/first.bsky.social/post/p1 and https://bsky.app/profile/second.bsky.social/post/p2 nice";

        var matches = site.Match(content);

        Assert.Equal(2, matches.Count);

        Assert.Equal("first.bsky.social", matches[0].Groups["user"].Value);
        Assert.Equal("p1", matches[0].Groups["id"].Value);

        Assert.Equal("second.bsky.social", matches[1].Groups["user"].Value);
        Assert.Equal("p2", matches[1].Groups["id"].Value);
    }
}
