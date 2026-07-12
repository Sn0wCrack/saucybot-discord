using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using SaucyBot.Site.Response;

namespace SaucyBot.Site;

public interface IBaseSite
{
    string Identifier { get; }
    Color Color { get; }
    MatchCollection Match(string message);
    Task<ProcessResponse?> Process(ProcessRequest request);
}
