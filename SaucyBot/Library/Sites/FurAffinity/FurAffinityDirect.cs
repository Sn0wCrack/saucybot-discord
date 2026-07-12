using System.Globalization;
using System.Net;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Polly;
using Polly.Fallback;
using Polly.Retry;
using SaucyBot.Services;

namespace SaucyBot.Library.Sites.FurAffinity;

public class FurAffinityDirect : IFurAffinityClient
{
    private const string BaseUrl = "https://furaffinity.net";

    private readonly ICacheManager _cache;

    private readonly HttpClient _client;

    private readonly ILogger<FurAffinityDirect> _logger;

    private readonly ResiliencePipeline<string?> _pipeline;

    public FurAffinityDirect(ICacheManager cacheManager, HttpClient client, ILogger<FurAffinityDirect> logger)
    {
        _cache = cacheManager;
        _client = client;
        _logger = logger;

        _pipeline = new ResiliencePipelineBuilder<string?>()
            .AddFallback(new FallbackStrategyOptions<string?>
            {
                FallbackAction = _ => Outcome.FromResultAsValueTask<string?>(null),
                ShouldHandle = arguments => arguments.Outcome switch
                {
                    { Exception: HttpRequestException e } => e.StatusCode == HttpStatusCode.NotFound ? PredicateResult.True() : PredicateResult.False(),
                    _ => PredicateResult.False(),
                }
            })
            .AddRetry(new RetryStrategyOptions<string?>
            {
                ShouldHandle = arguments => arguments.Outcome switch
                {
                    { Exception: HttpRequestException e } => e.StatusCode >= HttpStatusCode.InternalServerError ? PredicateResult.True() : PredicateResult.False(),
                    _ => PredicateResult.False(),
                },
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(3)
            })
            .AddTimeout(TimeSpan.FromSeconds(15))
            .Build();
    }

    public async Task<FaExportSubmission?> GetSubmission(string identifier)
    {
        var url = $"{BaseUrl}/view/{identifier}";

        var response = await _cache.Remember(
            $"furaffinity_direct.submission_response_{identifier}",
            TimeSpan.FromDays(7),
            async () => await _pipeline.ExecuteAsync(async token => await _client.GetStringAsync(url, token))
        );

        if (response is null)
        {
            return null;
        }

        var parsed = await _cache.Remember(
            $"furaffinity_direct.submission_parsed_{identifier}",
            TimeSpan.FromDays(7),
            () => Task.FromResult<FaExportSubmission?>(new FurAffinitySubmissionPage(response, identifier).ToFaExportSubmission())
        );

        return parsed;
    }
}

public sealed class FurAffinitySubmissionPage
{
    private const string BaseUrl = "https://furaffinity.net";

    private readonly IHtmlDocument _document;
    private readonly string _identifier;

    public FurAffinitySubmissionPage(string page, string identifier)
    {
        var parser = new HtmlParser();
        _document = parser.ParseDocument(page);
        _identifier = identifier;
    }

    private static string LastPath(string path) => path.Split('/').Last();

    private static string PickDate(IElement element)
    {
        var text = element.TextContent;
        return text.Contains("ago") ? element.GetAttribute("title") ?? text : text;
    }

    private static string ToIso8601(string date)
    {
        var parsed = DateTime.SpecifyKind(
            DateTime.ParseExact(date, "MMMM dd, yyyy HH:mm:ss tt", CultureInfo.InvariantCulture),
            DateTimeKind.Utc
        );

        return parsed.ToString("o");
    }

    private static string GetPageStat(IElement? statsContainer, string title)
    {
        if (statsContainer is null) return "";
        var statDiv = statsContainer.QuerySelector($"div[title=\"{title}\"]");
        return statDiv?.QuerySelector("div")?.TextContent?.Trim() ?? "";
    }

    private static string GetRating(IElement? statsContainer)
    {
        if (statsContainer is null) return "";
        var ratingChild = statsContainer.QuerySelector("[class*=\"c-contentRating\"]");
        return ratingChild?.TextContent?.Trim() ?? "";
    }

    private static Dictionary<string, string> ParseContentStats(IHtmlDocument document)
    {
        var result = new Dictionary<string, string>();
        var statsContainer = document.QuerySelector(".submission-content-stats");
        if (statsContainer is null) return result;

        var highlightSpan = statsContainer.QuerySelector(":scope > span.highlight");
        var valueSpan = statsContainer.QuerySelectorAll(":scope > span:not(.highlight)").FirstOrDefault();

        if (highlightSpan is null || valueSpan is null) return result;

        var labels = highlightSpan.QuerySelectorAll("span").Select(s => s.TextContent.Trim()).ToArray();
        var values = valueSpan.QuerySelectorAll("span").Select(s => s.TextContent.Trim()).ToArray();

        for (var i = 0; i < Math.Min(labels.Length, values.Length); i++)
        {
            result[labels[i]] = values[i];
        }

        return result;
    }

    public FaExportSubmission ToFaExportSubmission()
    {
        var title = _document.QuerySelector("meta[property=\"og:title\"]")?.GetAttribute("content") ?? "";

        var descriptionHtml = _document.QuerySelector(".submission-description-text")?.InnerHtml?.Trim() ?? "";

        var artistLink = _document.QuerySelector(".submission-description-artist .c-usernameBlockSimple a[href^=\"/user/\"]")
                      ?? _document.QuerySelector(".submission-description-artist a[href^=\"/user/\"]");
        var artistName = artistLink?.QuerySelector(".c-usernameBlockSimple__displayName")?.TextContent?.Trim()
                      ?? artistLink?.TextContent?.Trim() ?? "";
        var artistHref = artistLink?.GetAttribute("href")?.TrimStart('/') ?? "";
        var profileUrl = $"{BaseUrl}/{artistHref}";

        var avatarSrc = _document.QuerySelector(".submission-description-artist img.avatar")?.GetAttribute("src");

        var dateElement = _document.QuerySelector(".submission-description-artist .popup_date");
        var date = dateElement is not null ? PickDate(dateElement) : "";
        var postedAt = string.IsNullOrEmpty(date) ? "" : ToIso8601(date);

        var img = _document.QuerySelector("img#submissionImg");

        var downloadLink = _document.QuerySelector("#submission-options a[href*=\"d.furaffinity.net\"]");
        var downloadUrl = downloadLink is not null ? $"https:{downloadLink.GetAttribute("href")}" : "";

        var ogThumb = _document.QuerySelector("meta[property=\"og:image\"]");
        var ogThumbContent = ogThumb?.GetAttribute("content") ?? "";
        string thumbImg;
        if (string.IsNullOrEmpty(ogThumbContent) || ogThumbContent.Contains("/banners/fa_logo"))
        {
            thumbImg = img is not null ? $"https:{img.GetAttribute("data-preview-src")}" : "";
        }
        else
        {
            thumbImg = ogThumbContent.Replace("http:", "https:");
        }

        var statsContainer = _document.QuerySelector(".submission-page-stats");
        var views = GetPageStat(statsContainer, "Views");
        var commentsCount = GetPageStat(statsContainer, "Comments");
        var favorites = GetPageStat(statsContainer, "Favorites");
        var rating = GetRating(statsContainer);

        var contentStats = ParseContentStats(_document);

        var keywords = _document.QuerySelectorAll(".submission-tags a[href*=\"/search/@keywords\"]")
            .Select(a => a.TextContent.Trim())
            .Where(k => !string.IsNullOrEmpty(k))
            .ToArray();

        var link = _document.QuerySelector("meta[property=\"og:url\"]")?.GetAttribute("content")
                ?? $"{BaseUrl}/view/{_identifier}/";

        return new FaExportSubmission(
            Title: title,
            Description: descriptionHtml,
            DescriptionBody: descriptionHtml,
            Name: artistName,
            Profile: profileUrl,
            ProfileName: LastPath(artistHref),
            Avatar: avatarSrc is not null ? $"https:{avatarSrc}" : "",
            Link: link,
            Posted: date,
            PostedAt: postedAt,
            Download: downloadUrl,
            Full: img is not null ? $"https:{img.GetAttribute("data-fullview-src")}" : "",
            Thumbnail: thumbImg,
            Category: contentStats.GetValueOrDefault("Category", ""),
            Theme: contentStats.GetValueOrDefault("Theme", ""),
            Species: contentStats.GetValueOrDefault("Species", ""),
            Gender: contentStats.GetValueOrDefault("Gender", ""),
            Favorites: favorites,
            Comments: commentsCount,
            Views: views,
            Resolution: contentStats.GetValueOrDefault("Resolution", ""),
            Rating: rating,
            Keywords: keywords
        );
    }
}
