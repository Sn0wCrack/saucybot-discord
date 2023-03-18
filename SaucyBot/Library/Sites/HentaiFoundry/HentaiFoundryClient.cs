using System.Net;
using System.Net.Http.Headers;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using SaucyBot.Services;

namespace SaucyBot.Library.Sites.HentaiFoundry;

public sealed class HentaiFoundryClient
{
    private const string BaseUrl = "https://www.hentai-foundry.com";
    
    private readonly ILogger<HentaiFoundryClient> _logger;
    private readonly IConfiguration _configuration;
    private readonly ICacheManager _cache;

    private readonly CookieContainer _cookieContainer = new();
    private readonly HttpClient _client;
    
    public HentaiFoundryClient(
        ILogger<HentaiFoundryClient> logger,
        IConfiguration configuration,
        ICacheManager cacheManager
    ) {
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

    public async Task<bool> Agree()
    {
        await _client.GetAsync($"{BaseUrl}/?enterAgree=1");

        return true;
    }

    public async Task<HentaiFoundryPicture?> GetPage(string url)
    {
        var response = await _cache.Remember(
            $"hentaifoundry.picture_{url}",
            async () => await _client.GetStringAsync(url)
        );

        return response is null ? null : new HentaiFoundryPicture(response);
    }
}

public sealed class HentaiFoundryPicture
{
    private const string BaseUrl = "https://www.hentai-foundry.com";
    
    private readonly IHtmlDocument _document;
    
    public HentaiFoundryPicture(string page)
    {
        var parser = new HtmlParser();

        _document = parser.ParseDocument(page);
    }

    public string? Title() => _document.QuerySelector(".imageTitle")?.TextContent;
    public string? Description() => _document.QuerySelector(".picDescript")?.TextContent;
    public string? ImageUrl() => $"https:{_document.QuerySelector("#picBox .boxbody img")?.GetAttribute("src")}";
    public string? AuthorName() => _document.QuerySelector("#descriptionBox .boxbody a img")?.GetAttribute("title");
    public string AuthorUrl() =>
        $"{BaseUrl}{_document.QuerySelector("#descriptionBox .boxbody a")?.GetAttribute("href")}";
    public string? AuthorAvatarUrl() =>
        $"https:{_document.QuerySelector("#descriptionBox .boxbody a img")?.GetAttribute("src")}";

    public DateTimeOffset? PostedAt()
    {
        var datetime = _document.QuerySelector("#pictureGeneralInfoBox time")?.GetAttribute("datetime");

        if (datetime is null)
        {
            return null;
        }

        return DateTimeOffset.Parse(datetime);
    }
    
    public string Views()
    {
        var views = _document.QuerySelector("#pictureGeneralInfoBox .boxbody .column span:contains('Views')")
            ?.NextSibling
            ?.TextContent
            .Trim();

        return views ?? "0";
    }

    public string Votes()
    {
        var votes = _document.QuerySelector("#pictureGeneralInfoBox .boxbody .column span:contains('Vote Score')")
            ?.NextSibling
            ?.TextContent
            .Trim();

        return votes ?? "0";
    }
}
