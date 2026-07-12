using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SaucyBot.Library.Sites.ExHentai;
using SaucyBot.Site;
using Xunit;

namespace SaucyBot.Tests.Unit.Site;

public class ExHentaiTest
{
    [Fact]
    public async Task AnEmbedIsCreatedForGallery()
    {
        var logger = Substitute.For<ILogger<ExHentai>>();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                {"Sites:ExHentai:Cookies:MemberId", "12345"},
                {"Sites:ExHentai:Cookies:PasswordHash", "abcdef"}
            })
            .Build();

        var client = Substitute.For<IExHentaiClient>();

        var html = @"
            <html>
                <div class='gm'>
                    <h1 id='gn'>Test Gallery Title</h1>
                    <div id='gmid'>
                        <div id='gd1'><div style='url(https://example.com/image.jpg)'></div></div>
                        <div id='gdn'><a href='https://exhentai.org/uploader/testuser'>TestAuthor</a></div>
                        <div id='gd3'>
                            <div id='gd4'>
                                <div id='gdd'>
                                    <table><tbody>
                                        <tr><td>Language:</td><td>English</td></tr>
                                        <tr><td>Length:</td><td>20 pages</td></tr>
                                        <tr><td>Posted:</td><td>2024-01-01 00:00</td></tr>
                                    </tbody></table>
                                </div>
                            </div>
                        </div>
                    </div>
                    <div id='comment_0'>Test description</div>
                    <div id='rating_label'>Average: 4.5</div>
                </div>
            </html>";

        var page = new ExHentaiGalleryPage(html);

        client
            .GetGallery(Arg.Any<ExHentaiGalleryRequest>())
            .Returns(page);

        var site = new ExHentai(logger, config, client);

        var matches = site.Match("https://exhentai.org/g/12345/abcdef123/");
        var match = matches[0];

        var result = await site.Process(new ProcessRequest(match));

        Assert.NotNull(result);
        Assert.NotEmpty(result.Embeds);
        Assert.Single(result.Embeds);
    }

    [Fact]
    public async Task NothingIsReturnedWhenTheApiClientReturnsUnsuccessfully()
    {
        var logger = Substitute.For<ILogger<ExHentai>>();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                {"Sites:ExHentai:Cookies:MemberId", "12345"},
                {"Sites:ExHentai:Cookies:PasswordHash", "abcdef"}
            })
            .Build();

        var client = Substitute.For<IExHentaiClient>();

        client
            .GetGallery(Arg.Any<ExHentaiGalleryRequest>())
            .Returns((ExHentaiGalleryPage?)null);

        var site = new ExHentai(logger, config, client);

        var matches = site.Match("https://exhentai.org/g/12345/abcdef123/");
        var match = matches[0];

        var result = await site.Process(new ProcessRequest(match));

        Assert.Null(result);
    }

    [Fact]
    public async Task NothingIsReturnedWhenExHentaiLinksNotConfigured()
    {
        var logger = Substitute.For<ILogger<ExHentai>>();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                {"Sites:ExHentai:Cookies:MemberId", ""},
                {"Sites:ExHentai:Cookies:PasswordHash", ""}
            })
            .Build();

        var client = Substitute.For<IExHentaiClient>();

        var site = new ExHentai(logger, config, client);

        var matches = site.Match("https://exhentai.org/g/12345/abcdef123/");
        var match = matches[0];

        var result = await site.Process(new ProcessRequest(match));

        Assert.Null(result);
    }
}
