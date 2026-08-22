using System.Text.RegularExpressions;
using Discord;

namespace SaucyBot.Site;

public interface IBaseSite
{
    string Identifier { get; }
    Color Color { get; }
    Regex Pattern { get; }
    Task<ProcessResponse?> Process(ProcessRequest request);
}
