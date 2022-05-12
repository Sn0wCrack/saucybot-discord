using System.Text.RegularExpressions;
using Discord.WebSocket;
using SaucyBot.Site.Response;

namespace SaucyBot.Site;

public class Newgrounds : BaseSite
{
    public override string Identifier => "Newgrounds";
    
    public override Task<ProcessResponse?> Process(Match match, SocketUserMessage? message = null)
    {
        throw new NotImplementedException();
    }
}
