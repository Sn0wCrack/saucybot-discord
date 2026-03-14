using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using SaucyBot.Site.Response;

namespace SaucyBot.Site;

public sealed class Instagram : BaseSite
{
    public override string Identifier => "Instagram";

    // Match Instagram URLs from instagram.com, www.instagram.com, m.instagram.com
    // Only capture /p/, /reel/, /reels/ paths
    protected override string Pattern => 
        @"https?:\/\/(?<host>(?:www\.|m\.)?instagram\.com)\/(?<path>(?:p|reel|reels)\/[^\/\s\?\#]+(?:\/[^\s\?\#]*)?)(?<query>\?[^\s\#]*)?(?<fragment>\#[^\s]*)?";

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

        var rewrittenUrl = $"https://d.vxinstagram.com/{path}{query}{fragment}";

        _logger.LogDebug("Rewrote Instagram URL: {Original} -> {Rewritten}", originalUrl, rewrittenUrl);

        return Task.FromResult<ProcessResponse?>(new ProcessResponse { Text = rewrittenUrl });
    }
}
