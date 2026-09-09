using SaucyBot.Site.Pixiv;

namespace SaucyBot.Options.Sites;

public sealed class PixivOptions
{
    public string? SessionCookie { get; init; }
    public int PostLimit { get; init; } = 5;

    public UgoiraOptions Ugoira { get; init; } = new();
}

public sealed class UgoiraOptions
{
    public UgoiraCodec Codec { get; init; } = UgoiraCodec.H264;
    public int Bitrate { get; init; } = 2000;
    public int Preset { get; init; } = 6;
    public int Crf { get; init; } = 40;
}
