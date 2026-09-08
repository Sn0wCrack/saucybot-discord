using SaucyBot.Site.Pixiv;

namespace SaucyBot.Options.Sites;

public sealed class PixivOptions
{
    public string? SessionCookie { get; init; }
    public int PostLimit { get; init; }

    public UgoiraOptions Ugoira { get; init; } = new();
}

public sealed class UgoiraOptions
{
    public UgoiraCodec? Codec { get; init; }
    public int? Bitrate { get; init; }
    public int? Preset { get; init; }
    public int? Crf { get; init; }
}
