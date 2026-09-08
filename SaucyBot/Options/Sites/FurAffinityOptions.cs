namespace SaucyBot.Options.Sites;

public sealed class FurAffinityOptions
{
    public FurAffinityCookiesOptions Cookies { get; init; } = new();
}

public sealed class FurAffinityCookiesOptions
{
    public string? A { get; init; }
    public string? B { get; init; }
}
