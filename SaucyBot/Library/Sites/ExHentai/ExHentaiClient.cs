using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Css.Dom;
using AngleSharp.Dom;
using SaucyBot.Library.Sites.HentaiFoundry;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using SaucyBot.Services;
using IConfiguration = Microsoft.Extensions.Configuration.IConfiguration;

namespace SaucyBot.Library.Sites.ExHentai;

public class ExHentaiClient
{
    private readonly ILogger<ExHentaiClient> _logger;
    private readonly IConfiguration _configuration;
    private readonly CacheManager _cache;

    private readonly CookieContainer _cookieContainer = new();
    private readonly HttpClient _client;

    public ExHentaiClient(
        ILogger<ExHentaiClient> logger,
        IConfiguration configuration,
        CacheManager cacheManager
    )  {
        _logger = logger;
        _configuration = configuration;
        _cache = cacheManager;
        
        var httpClientHandler = new HttpClientHandler
        {
            CookieContainer = _cookieContainer,
            UseCookies = true,
            AllowAutoRedirect = true,
        };
        
        _client = new HttpClient(httpClientHandler);
        
        _client.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/51.0.2704.103 Safari/537.36");
        
        _client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json")
        );
        
        _client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/html")    
        );
    }

    public async Task<ExHentaiGallery?> GetGallery(string url)
    {
        var response = await _cache.Remember(
            $"exhentai.gallery_{url}",
            async () => await _client.GetStringAsync(url)
        );

        return response is null ? null : new ExHentaiGallery(response);
    }
}

public class ExHentaiGallery
{
    private readonly IHtmlDocument _document;

    public ExHentaiGallery(string page)
    {
        var configuration = Configuration.Default.WithCss();

        var context = BrowsingContext.New(configuration);
        
        var parser = new HtmlParser(default, context);

        _document = parser.ParseDocument(page);
    }

    public string? Title() => _document.QuerySelector(".gm h1#gn")?.TextContent;
    public string? Description() => _document.QuerySelector("div#comment_0")?.GetInnerText();

    public string? Rating() => _document.QuerySelector("td#rating_label")?.TextContent.Replace("Average:", "");

    public string? AuthorUrl() => _document.QuerySelector(".gm #gmid #gdn a")?.GetAttribute("href");

    public string? ImageUrl()
    {
        var style = _document.QuerySelector(".gm #gd1 > div")?.GetAttribute("style");

        if (style is null)
        {
            return null;
        }

        var match = Regex.Match(style, @"url\((?<url>.*)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        return match.Groups["url"].Value;
    }

    public DateTimeOffset? PostedAt()
    {
        var dateTime = MetaContainer()?.Closest("tr")?.FirstElementChild?.Closest(".gdt2")?.TextContent;

        if (dateTime is null)
        {
            return null;
        }
        
        return DateTimeOffset.Parse(dateTime);
    }

    private IElement? MetaContainer() => _document.QuerySelector(".gm #gmid #gd3 #gdd tbody");
}
