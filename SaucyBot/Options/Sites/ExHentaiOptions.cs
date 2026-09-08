namespace SaucyBot.Options.Sites;

public sealed class ExHentaiOptions
{
    public ExHentaiCookiesOptions Cookies { get; init; } = new();
}

public sealed class ExHentaiCookiesOptions
{
    public string? MemberId { get; init; }
    public string? PasswordHash { get; init; }
}
