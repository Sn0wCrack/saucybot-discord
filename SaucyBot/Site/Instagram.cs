using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using SaucyBot.Site.Response;

namespace SaucyBot.Site;

public sealed partial class Instagram : BaseSite
{
    public override string Identifier => "Instagram";

    [GeneratedRegex(@"https?://(?<host>(?:www\.|m\.)?instagram\.com)/(?<path>(?:p|reel|reels)/[^/\s?#]+(?:/[^\s?#]*)?)(?<query>\?[^\s#]*)?(?<fragment>\#[^\s]*)?", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex InstagramPattern();

    protected override Regex Pattern => InstagramPattern();

    protected override Color Color => new(0xE4405F);

    private readonly ILogger<Instagram> _logger;

    public Instagram(ILogger<Instagram> logger)
    {
        _logger = logger;
    }

    public override Task<ProcessResponse?> Process(Match match, SocketUserMessage? message = null)
    {
        var originalUrl = match.Value;
        var path = match.Groups["path"].Value;
        var query = match.Groups["query"].Success ? match.Groups["query"].Value : string.Empty;
        var fragment = match.Groups["fragment"].Success ? match.Groups["fragment"].Value : string.Empty;

        var rewrittenUrl = $"https://vxinstagram.com/{path}{query}{fragment}";

        _logger.LogDebug("Rewrote Instagram URL: {Original} -> {Rewritten}", originalUrl, rewrittenUrl);

        return Task.FromResult<ProcessResponse?>(new ProcessResponse { Text = rewrittenUrl });
    }
}
