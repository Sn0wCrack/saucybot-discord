namespace SaucyBot.Site;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class SiteIdentifierAttribute(string identifier) : Attribute
{
    public string Identifier { get; } = identifier;
}
