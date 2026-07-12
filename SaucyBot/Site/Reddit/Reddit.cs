using System.Net;
using System.Text.RegularExpressions;
using Discord;
using SaucyBot.Site.Response;

namespace SaucyBot.Site.Reddit;

using SaucyBot.Site;

public sealed partial class RedditSite : BaseSite, IRedditSite
{
    public override string Identifier => "Reddit";

    [GeneratedRegex(@"https?://(www\.)?reddit\.com/media\?url=(?<url>[A-Z0-9\%\.]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex RedditPattern();

    public override Regex Pattern => RedditPattern();

    public override Color Color => new(0xFF4500);

    private readonly ILogger<RedditSite> _logger;

    public RedditSite(ILogger<RedditSite> logger)
    {
        _logger = logger;
    }

    public override async Task<ProcessResponse?> Process(ProcessRequest request)
    {
        // TODO: Handle v.redd.it links using youtube-dl or similar

        var response = new ProcessResponse();

        var originalUrl = WebUtility.UrlDecode(request.Match.Groups["url"].Value);

        response.Text = originalUrl;

        return response;
    }
}
