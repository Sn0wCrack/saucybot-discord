using System.Text.RegularExpressions;
using Discord.WebSocket;
using SaucyBot.Site.Response;

namespace SaucyBot.Site;

public class E621 : BaseSite
{
    public override string Identifier => "E621";
    
    public override Task<ProcessResponse?> Process(Match match, SocketUserMessage? message = null)
    {
        throw new NotImplementedException();
    }
}
