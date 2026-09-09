using System;
using SaucyBot.Options;
using SaucyBot.Options.Sites;
using SaucyBot.Services.Cache;
using SaucyBot.Site.Pixiv;
using Xunit;

namespace SaucyBot.Tests.Unit.Options;

public sealed class OptionsDefaultTest
{
    [Fact]
    public void CacheOptionsFallBacksToTheMemoryDriver()
    {
        var options = new CacheOptions();

        Assert.Equal(CacheDriverType.Memory, options.Driver);
    }

    [Theory]
    [InlineData(typeof(MemoryCacheOptions))]
    [InlineData(typeof(RedisCacheOptions))]
    [InlineData(typeof(HybridCacheOptions))]
    public void CacheLifetimesDefaultToOneHour(Type type)
    {
        var options = (dynamic)Activator.CreateInstance(type)!;

        Assert.Equal(3600, options.DefaultLifetime);
    }

    [Fact]
    public void PixivOptionsDefaultToAHumblePostLimit()
    {
        var options = new PixivOptions();

        Assert.Equal(5, options.PostLimit);
    }

    [Fact]
    public void ArtStationOptionsDefaultToAHumblePostLimit()
    {
        var options = new ArtStationOptions();

        Assert.Equal(5, options.PostLimit);
    }

    [Fact]
    public void UgoiraDefaultsToH264WithTwoMegabitBitrate()
    {
        var options = new UgoiraOptions();

        Assert.Equal(UgoiraCodec.H264, options.Codec);
        Assert.Equal(2000, options.Bitrate);
    }

    [Fact]
    public void UgoiraAv1DefaultsToPresetSixAndCrfForty()
    {
        var options = new UgoiraOptions();

        Assert.Equal(6, options.Preset);
        Assert.Equal(40, options.Crf);
    }

    [Theory]
    [InlineData(typeof(BlueskyOptions))]
    [InlineData(typeof(MisskeyOptions))]
    public void SiteDelaysDefaultToTwoSeconds(Type type)
    {
        var options = (dynamic)Activator.CreateInstance(type)!;

        Assert.Equal(2, options.Delay);
    }

    [Fact]
    public void SentrySampleRateDefaultsToSamplingEverything()
    {
        var options = new SentryOptions();

        Assert.Null(options.SampleRate);
    }

    [Fact]
    public void BotOptionsDefaultToEightMaximumEmbeds()
    {
        var options = new BotOptions();

        Assert.Equal(8u, options.MaximumEmbeds);
    }
}
