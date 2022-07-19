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

    private const string ExHentaiDomain = "exhentai.org";
    private const string EHentaiDomain = "e-hentai.org";

    public ExHentaiClient(
        ILogger<ExHentaiClient> logger,
        IConfiguration configuration,
        CacheManager cacheManager
    )  {
        _logger = logger;
        _configuration = configuration;
        _cache = cacheManager;
        
        _cookieContainer.Add(
            new Cookie(
                "ipb_member_id", 
                _configuration.GetSection("Sites:ExHentai:Cookies:MemberId").Get<string>(),
                "/",
                ExHentaiDomain
            )
         );
        
        _cookieContainer.Add(
            new Cookie(
                "ipb_pass_hash", 
                _configuration.GetSection("Sites:ExHentai:Cookies:PasswordHash").Get<string>(),
                "/",
                ExHentaiDomain
            )
        );
        
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

    public async Task<ExHentaiGalleryPage?> GetGallery(ExHentaiGalleryRequest request)
    {
        var response = await _cache.Remember(
            $"exhentai.gallery_{request.Id}_{request.Hash}",
            async () => await _client.GetStringAsync(request.GetUrl())
        );

        return response is null ? null : new ExHentaiGalleryPage(response);
    }
}

public enum ExHentaiRequestMode
{
    EHentai,
    ExHentai,
}

public abstract record ExHentaiRequest(ExHentaiRequestMode Mode)
{
    protected string GetBaseUrl()
    {
        return Mode switch
        {
            ExHentaiRequestMode.EHentai => "https://e-hentai.org",
            ExHentaiRequestMode.ExHentai => "https://exhentai.org",
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    public abstract string GetUrl();
}

public record ExHentaiGalleryRequest(ExHentaiRequestMode Mode, string Id, string Hash) : ExHentaiRequest(Mode)
{
    public override string GetUrl()
    {
        return $"{GetBaseUrl()}/g/{Id}/{Hash}";
    }
}

public class ExHentaiGalleryPage
{
    private readonly IHtmlDocument _document;

    public ExHentaiGalleryPage(string page)
    {
        var configuration = Configuration.Default.WithCss();

        var context = BrowsingContext.New(configuration);
        
        var parser = new HtmlParser(default, context);

        _document = parser.ParseDocument(page);
    }

    public string? Title() => _document.QuerySelector(".gm h1#gn")?.TextContent;
    public string? Description() => _document.QuerySelector("div#comment_0")?.GetInnerText();

    public string? Rating() => _document.QuerySelector("td#rating_label")?.TextContent.Replace("Average:", "").Trim();

    public string? Language() =>
        MetaContainer()?.QuerySelector("tr > td:contains('Language:')")?.NextSibling?.TextContent;

    public string? Length() =>
        MetaContainer()?.QuerySelector("tr > td:contains('Length:')")?.NextSibling?.TextContent.Replace("pages", "").Trim();
    
    public string? AuthorName() => _document.QuerySelector(".gm #gmid #gdn a")?.TextContent;
    
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
        var dateTime = MetaContainer()?.QuerySelector("tr > td:contains('Posted:')")?.NextSibling?.TextContent;

        if (dateTime is null)
        {
            return null;
        }
        
        return DateTimeOffset.Parse(dateTime);
    }

    private IElement? MetaContainer() => _document.QuerySelector(".gm #gmid #gd3 #gdd tbody");
}
