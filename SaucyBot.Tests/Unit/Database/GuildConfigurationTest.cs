using SaucyBot.Database.Models;
using Xunit;

namespace SaucyBot.Tests.Unit.Database;

public sealed class GuildConfigurationTest
{
    [Fact]
    public void IsSiteDisabledReturnsTrueForDisabledSite()
    {
        var configuration = new GuildConfiguration
        {
            DisabledSites =
            [
                new GuildConfigurationDisabledSite { Site = "Pixiv" }
            ]
        };

        Assert.True(configuration.IsSiteDisabled("Pixiv"));
    }

    [Fact]
    public void IsSiteDisabledIsCaseInsensitive()
    {
        var configuration = new GuildConfiguration
        {
            DisabledSites =
            [
                new GuildConfigurationDisabledSite { Site = "fxtwitter" }
            ]
        };

        Assert.True(configuration.IsSiteDisabled("FxTwitter"));
    }

    [Fact]
    public void IsSiteDisabledReturnsFalseForEnabledSite()
    {
        var configuration = new GuildConfiguration
        {
            DisabledSites =
            [
                new GuildConfigurationDisabledSite { Site = "Pixiv" }
            ]
        };

        Assert.False(configuration.IsSiteDisabled("DeviantArt"));
    }
}
