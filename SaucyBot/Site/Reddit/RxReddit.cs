using System.Net;
using System.Text.RegularExpressions;
using Discord;
using SaucyBot.Site.Response;

namespace SaucyBot.Site.Reddit;


public sealed partial class RxRedditSite : BaseSite, IRedditSite
{
    public override string Identifier => "Reddit";

    [GeneratedRegex(@"https?://(www\.)?reddit\.com/(r/(?<subreddit>\S+)/(comments|s)/(?<id>\S+)|media\?url=(?<url>[A-Z0-9\%\.]+))", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex RxRedditPattern();

    public override Regex Pattern => RxRedditPattern();

    public override Color Color => new(0xFF4500);

    private readonly ILogger<RxRedditSite> _logger;

    public RxRedditSite(ILogger<RxRedditSite> logger)
    {
        _logger = logger;
    }

    public override async Task<ProcessResponse?> Process(ProcessRequest request)
    {
        // Handle mangled URLs first if match exists
        return request.Match.Groups["url"].Success
            ? HandleMangledUrl(request.Match.Groups["url"].Value)
            : HandleRedditPost(request.Match.Groups["subreddit"], request.Match.Groups["id"]);
    }

    private static ProcessResponse? HandleRedditPost(Group subreddit, Group id)
    {
        if (!subreddit.Success || !id.Success)
        {
            return null;
        }

        return new ProcessResponse()
        {
            Text = $"https://rxddit.com/r/{subreddit.Value}/comments/{id.Value}"
        };
    }

    private static ProcessResponse HandleMangledUrl(string url)
    {
        return new ProcessResponse { Text = WebUtility.UrlDecode(url) };
    }
}
