using System.Text.RegularExpressions;
using Discord;

namespace SaucyBot.Site.Instagram;


[SiteIdentifier("Instagram")]
public sealed partial class VxInstagramSite : BaseSite
{
    [GeneratedRegex(@"https?://(?<host>(?:www\.|m\.)?instagram\.com)/(?<path>(?:p|reel|reels)/[^/\s?#]+(?:/[^\s?#]*)?)(?<query>\?[^\s#]*)?(?<fragment>\#[^\s]*)?", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex InstagramPattern();

    public override Regex Pattern => InstagramPattern();

    public override Color Color => new(0xE4405F);

    private readonly ILogger<VxInstagramSite> _logger;

    public VxInstagramSite(ILogger<VxInstagramSite> logger)
    {
        _logger = logger;
    }

    public override Task<ProcessResponse?> Process(ProcessRequest request)
    {
        var originalUrl = request.Match.Value;
        var path = request.Match.Groups["path"].Value;
        var query = request.Match.Groups["query"].Value;
        var fragment = request.Match.Groups["fragment"].Value;

        var rewrittenUrl = $"https://instagramfix.com/{path}{query}{fragment}";

        _logger.LogDebug("Rewrote Instagram URL: {Original} -> {Rewritten}", originalUrl, rewrittenUrl);

        return Task.FromResult<ProcessResponse?>(new ProcessResponse { Text = rewrittenUrl });
    }
}
