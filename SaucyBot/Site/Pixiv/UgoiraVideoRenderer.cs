using Microsoft.Extensions.Options;
using SaucyBot.Options.Sites;
using Xabe.FFmpeg;

namespace SaucyBot.Site.Pixiv;

public sealed class UgoiraVideoRenderer(IOptions<PixivOptions> pixivOptions) : IUgoiraVideoRenderer
{
    public async Task RenderAsync(string concatFile, string videoFile, CancellationToken cancellationToken)
    {
        var conversion = FFmpeg.Conversions.New()
            .SetOverwriteOutput(true)
            .AddParameter("-f concat", ParameterPosition.PreInput)
            .AddParameter($"-i \"{concatFile}\"", ParameterPosition.PreInput)
            .AddParameter("-pix_fmt yuv420p")
            .AddParameter("-filter:v \"pad=ceil(iw/2)*2:ceil(ih/2)*2\"")
            .SetOutput(videoFile);

        var ugoiraOptions = pixivOptions.Value.Ugoira;
        var codec = ugoiraOptions.Codec ?? UgoiraCodec.H264;
        var bitrate = ugoiraOptions.Bitrate ?? 2_000;

        switch (codec)
        {
            default:
            case UgoiraCodec.H264:
                conversion
                    .AddParameter("-c:v libx264")
                    .AddParameter($"-b:v {bitrate}k");
                break;
            case UgoiraCodec.AV1:
                var preset = ugoiraOptions.Preset ?? 6;
                var crf = ugoiraOptions.Crf ?? 40;

                conversion
                    .AddParameter("-c:v libsvtav1")
                    .AddParameter($"-preset {preset}")
                    .AddParameter($"-crf {crf}")
                    .AddParameter($"-maxrate {bitrate}k")
                    .AddParameter($"-bufsize {bitrate * 2}k");
                break;
            case UgoiraCodec.VP9:
                conversion
                    .AddParameter("-c:v libvp9")
                    .AddParameter($"-b:v {bitrate}k");
                break;
        }

        await conversion.Start(cancellationToken);
    }
}
