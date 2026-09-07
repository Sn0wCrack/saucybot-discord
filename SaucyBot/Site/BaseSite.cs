using System.Reflection;
using System.Text.RegularExpressions;
using Discord;

namespace SaucyBot.Site;

public abstract class BaseSite : IBaseSite
{
    public string Identifier => GetType().GetCustomAttribute<SiteIdentifierAttribute>()?.Identifier
        ?? throw new InvalidOperationException($"Site type '{GetType().Name}' has no site identifier.");

    public virtual Regex Pattern { get; protected init; } = new(string.Empty);

    public virtual Color Color => Color.Default;

    public abstract Task<ProcessResponse?> Process(ProcessRequest request);
}
