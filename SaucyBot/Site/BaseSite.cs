using System.Diagnostics;
using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using SaucyBot.Site.Response;

namespace SaucyBot.Site;

public abstract class BaseSite
{
    public virtual string Identifier => "Base";

    protected virtual string Pattern => "base";

    protected virtual Color Color => Color.Default;

    public bool IsMatch(string message)
    {
        return Regex.IsMatch(message, Pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);
    }

    public MatchCollection Match(string message)
    {
        return Regex.Matches(message, Pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);
    }

    public abstract Task<ProcessResponse?> Process(Match match, SocketUserMessage? message = null);
}