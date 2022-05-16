using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using SaucyBot.Services;

namespace SaucyBot.Library.Sites.Newgrounds;

public class NewgroundsClient
{
    private const string BaseUrl = "https://www.newgrounds.com";
    
    private readonly ILogger<NewgroundsClient> _logger;
    private readonly IConfiguration _configuration;
    private readonly CacheManager _cache;

    private readonly HttpClient _client = new();

    public NewgroundsClient(
        ILogger<NewgroundsClient> logger,
        IConfiguration configuration,
        CacheManager cacheManager
    ) {
        _logger = logger;
        _configuration = configuration;
        _cache = cacheManager;
        
        _client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("SaucyBot", Assembly.GetEntryAssembly()?.GetName().Version?.ToString())    
        );
        
        _client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json")
        );
    }

    public async Task<NewgroundsArt?> GetArt(string user, string slug)
    {
        var url = $"{BaseUrl}/art/view/{user}/{slug}";
        
        var response = await _cache.Remember(
            $"newgrounds.art_{user}_{slug}",
            async () => await _client.GetStringAsync(url)
        );

        return response is null ? null : new NewgroundsArt(response);
    }
}

public class NewgroundsArt
{
    private readonly IHtmlDocument _document;
    
    public NewgroundsArt(string page)
    {
        var parser = new HtmlParser();

        _document = parser.ParseDocument(page);
    }

    public string? Title() => _document.QuerySelector(".body-guts .column.wide.right .pod-head h2")?.TextContent;

    public string? Description() => _document.QuerySelector("#author_comments")?.InnerHtml;
    
    public string? ImageUrl() => _document.QuerySelector(".pod-body .image #portal_item_view img")?.GetAttribute("src");

    public string Views() =>
        _document.QuerySelector(".sidestats dt:contains('Views')")?.NextElementSibling?.TextContent ?? "0";
    
    public string Score() => _document.QuerySelector("#score_number")?.TextContent ?? "0.00";
}
