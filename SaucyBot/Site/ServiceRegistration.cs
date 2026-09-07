using SaucyBot.Site.ArtStation;
using SaucyBot.Site.Bluesky;
using SaucyBot.Site.DeviantArt;
using SaucyBot.Site.E621;
using SaucyBot.Site.ExHentai;
using SaucyBot.Site.FurAffinity;
using SaucyBot.Site.HentaiFoundry;
using SaucyBot.Site.Instagram;
using SaucyBot.Site.Misskey;
using SaucyBot.Site.Newgrounds;
using SaucyBot.Site.Pixiv;
using SaucyBot.Site.Reddit;
using SaucyBot.Site.Twitter;

namespace SaucyBot.Site;

public static class SiteServiceRegistration
{
    public static IServiceCollection AddSaucyBotSites(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        AddSite<ArtStationSite>(services);
        AddSite<BlueskySite>(services);
        AddSite<DeviantArtSite>(services);
        AddSite<E621Site>(services);
        AddSite<ExHentaiSite>(services);
        AddSite<XFurAffinitySite>(services);
        AddSite<HentaiFoundrySite>(services);
        AddSite<VxInstagramSite>(services);
        AddSite<MisskeySite>(services);
        AddSite<NewgroundsSite>(services);
        AddSite<PixivSite>(services);
        AddSite<RedditSite>(services);
        AddSite<FxTwitterSite>(services);
        return services;
    }

    private static void AddSite<TSite>(IServiceCollection services)
        where TSite : class, IBaseSite
    {
        services.AddScoped<TSite>();
        services.AddSingleton(new SiteRegistration(typeof(TSite)));
    }
}

public sealed record SiteRegistration(Type ImplementationType);
