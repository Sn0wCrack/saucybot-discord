using System.Text.RegularExpressions;
using Discord;
using SaucyBot.Site.Response;

namespace SaucyBot.Site;

public abstract class BaseSite : IBaseSite
{
    public virtual string Identifier => "Base";

    public virtual Regex Pattern { get; init; } = new(string.Empty);

    public virtual Color Color => Color.Default;

    public abstract Task<ProcessResponse?> Process(ProcessRequest request);
}
