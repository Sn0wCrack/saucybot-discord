using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using SaucyBot.Site.Response;

namespace SaucyBot.Site;

public sealed partial class Instagram : BaseSite, IInstagramSite
{
    public override string Identifier => "Instagram";

    [GeneratedRegex(@"https?://(?<host>(?:www\.|m\.)?instagram\.com)/(?<path>(?:p|reel|reels)/[^/\s?#]+(?:/[^\s?#]*)?)(?<query>\?[^\s#]*)?(?<fragment>\#[^\s]*)?", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex InstagramPattern();

    protected override Regex Pattern => InstagramPattern();

    public override Color Color => new(0xE4405F);

    private readonly ILogger<Instagram> _logger;

    public Instagram(ILogger<Instagram> logger)
    {
        _logger = logger;
    }

    public override Task<ProcessResponse?> Process(ProcessRequest request)
    {
        var originalUrl = request.Match.Value;
        var path = request.Match.Groups["path"].Value;
        var query = request.Match.Groups["query"].Success ? request.Match.Groups["query"].Value : string.Empty;
        var fragment = request.Match.Groups["fragment"].Success ? request.Match.Groups["fragment"].Value : string.Empty;

        var rewrittenUrl = $"https://vxinstagram.com/{path}{query}{fragment}";

        _logger.LogDebug("Rewrote Instagram URL: {Original} -> {Rewritten}", originalUrl, rewrittenUrl);

        return Task.FromResult<ProcessResponse?>(new ProcessResponse { Text = rewrittenUrl });
    }
}
