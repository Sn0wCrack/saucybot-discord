using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using SaucyBot.Site.Response;

namespace SaucyBot.Site;

public abstract class BaseSite
{
    public virtual string Identifier => "Base";

    protected virtual Regex Pattern { get; init; } = new(string.Empty);

    protected virtual Color Color => Color.Default;
    
    public MatchCollection Match(string message)
    {
        return Pattern.Matches(message);
    }

    public abstract Task<ProcessResponse?> Process(Match match, SocketUserMessage? message = null);
}
