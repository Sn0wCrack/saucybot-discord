namespace SaucyBot.Options;

public sealed class SentryOptions
{
    public string? Dsn { get; set; }

    public float? SampleRate { get; set; } = null;
}
