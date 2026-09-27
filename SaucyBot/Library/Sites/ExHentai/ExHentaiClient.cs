using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Configuration;
using SaucyBot.Services;

namespace SaucyBot.Library.Sites.ExHentai;

public sealed class ExHentaiClient : IExHentaiClient
{
    private readonly ILogger<ExHentaiClient> _logger;
    private readonly IConfiguration _configuration;
    private readonly ICacheManager _cache;

    private readonly HttpClient _client;

    private const string ExHentaiDomain = "exhentai.org";
    private const string EHentaiDomain = "e-hentai.org";

    public ExHentaiClient(
        ILogger<ExHentaiClient> logger,
        IConfiguration configuration,
        ICacheManager cacheManager,
        HttpClient client
    )
    {
        _logger = logger;
        _configuration = configuration;
        _cache = cacheManager;
        _client = client;
    }

    public async Task<ExHentaiGalleryPage?> GetGallery(ExHentaiGalleryRequest request)
    {
        return await _cache.Remember(
            $"exhentai.gallery_{request.Id}_{request.Hash}",
            async () => new ExHentaiGalleryPage(await _client.GetStringAsync(request.GetUrl()))
        );
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

public sealed record ExHentaiGalleryRequest(ExHentaiRequestMode Mode, string Id, string Hash) : ExHentaiRequest(Mode)
{
    public override string GetUrl()
    {
        return $"{GetBaseUrl()}/g/{Id}/{Hash}";
    }
}

public sealed partial class ExHentaiGalleryPage
{
    public string? TitleValue { get; init; }
    public string? DescriptionValue { get; init; }
    public string? RatingValue { get; init; }
    public string? LanguageValue { get; init; }
    public string? LengthValue { get; init; }
    public string? AuthorNameValue { get; init; }
    public string? AuthorUrlValue { get; init; }
    public string? ImageUrlValue { get; init; }
    public DateTimeOffset? PostedAtValue { get; init; }

    [GeneratedRegex(@"url\((?<url>.*)\)", RegexOptions.IgnoreCase)]
    private static partial Regex CssBackgroundUrlRegex();

    public ExHentaiGalleryPage()
    {
    }

    public ExHentaiGalleryPage(string page)
    {
        var parser = new HtmlParser();
        var document = parser.ParseDocument(page);

        TitleValue = document.QuerySelector(".gm h1#gn")?.TextContent;
        DescriptionValue = document.QuerySelector("div#comment_0")?.TextContent;
        RatingValue = document.QuerySelector("td#rating_label")?.TextContent.Replace("Average:", "").Trim();

        var metadata = document.QuerySelector(".gm #gmid #gd3 #gdd tbody");
        LanguageValue = metadata?.QuerySelector("tr > td:contains('Language:')")?.NextSibling?.TextContent;
        LengthValue = metadata?.QuerySelector("tr > td:contains('Length:')")?.NextSibling?.TextContent.Replace("pages", "").Trim();

        var author = document.QuerySelector(".gm #gmid #gdn a");
        AuthorNameValue = author?.TextContent;
        AuthorUrlValue = author?.GetAttribute("href");

        var style = document.QuerySelector(".gm #gd1 > div")?.GetAttribute("style");
        if (style is not null)
        {
            var match = CssBackgroundUrlRegex().Match(style);
            ImageUrlValue = match.Groups["url"].Value;
        }

        var dateTime = metadata?.QuerySelector("tr > td:contains('Posted:')")?.NextSibling?.TextContent;
        PostedAtValue = dateTime is null ? null : DateTimeOffset.Parse(dateTime);
    }

    public string? Title() => TitleValue;
    public string? Description() => DescriptionValue;
    public string? Rating() => RatingValue;
    public string? Language() => LanguageValue;
    public string? Length() => LengthValue;
    public string? AuthorName() => AuthorNameValue;
    public string? AuthorUrl() => AuthorUrlValue;
    public string? ImageUrl() => ImageUrlValue;
    public DateTimeOffset? PostedAt() => PostedAtValue;
}
