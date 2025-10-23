using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using SaucyBot.Site.Response;

namespace SaucyBot.Site;

public sealed class Instagram : BaseSite
{
    public override string Identifier => "Instagram";

    // Match Instagram URLs from instagram.com, www.instagram.com, m.instagram.com, l.instagram.com
    // Only capture /p/, /reel/, /reels/ paths
    protected override string Pattern => 
        @"https?:\/\/(?<host>(?:www\.|m\.|l\.)?instagram\.com)\/(?<path>(?:p|reel|reels)\/[^\/\s\?\#]+(?:\/[^\s\?\#]*)?)(?<query>\?[^\s\#]*)?(?<fragment>\#[^\s]*)?";

    protected override Color Color => new(0xE4405F);

    private readonly ILogger<Instagram> _logger;

    public Instagram(ILogger<Instagram> logger)
    {
        _logger = logger;
    }

    public override async Task<ProcessResponse?> Process(Match match, SocketUserMessage? message = null)
    {
        var originalUrl = match.Value;
        var host = match.Groups["host"].Value.ToLowerInvariant();

        // Idempotent: skip if already rewritten
        if (host.Contains("kkinstagram.com"))
        {
            _logger.LogDebug("Instagram URL already rewritten to kkinstagram.com, skipping: {Url}", originalUrl);
            return null;
        }

        // Security: ensure exact host match (not subdomains like instagram.com.evil.tld)
        if (!IsValidInstagramHost(host))
        {
            _logger.LogDebug("Invalid Instagram host detected, skipping: {Host}", host);
            return null;
        }

        var path = match.Groups["path"].Value;
        var query = match.Groups["query"].Success ? match.Groups["query"].Value : string.Empty;
        var fragment = match.Groups["fragment"].Success ? match.Groups["fragment"].Value : string.Empty;

        // Check if path is in accepted list
        if (!IsAcceptedPath(path))
        {
            _logger.LogDebug("Instagram path not in accepted list (posts/reels), skipping: {Path}", path);
            return null;
        }

        // Rewrite URL to kkinstagram.com
        var rewrittenUrl = $"https://kkinstagram.com/{path}{query}{fragment}";

        _logger.LogDebug("Rewrote Instagram URL: {Original} -> {Rewritten}", originalUrl, rewrittenUrl);

        var response = new ProcessResponse
        {
            Text = rewrittenUrl
        };

        return response;
    }

    private bool IsValidInstagramHost(string host)
    {
        // Accepted hosts (case-insensitive): instagram.com, www.instagram.com, m.instagram.com, l.instagram.com
        var validHosts = new[]
        {
            "instagram.com",
            "www.instagram.com",
            "m.instagram.com",
            "l.instagram.com"
        };

        return validHosts.Contains(host.ToLowerInvariant());
    }

    private bool IsAcceptedPath(string path)
    {
        // Accepted paths: /p/, /reel/, /reels/
        var lowerPath = path.ToLowerInvariant();
        return lowerPath.StartsWith("p/") || lowerPath.StartsWith("reel/") || lowerPath.StartsWith("reels/");
    }
}
