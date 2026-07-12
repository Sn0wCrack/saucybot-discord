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
        services.AddSingleton<IArtStationSite, ArtStationSite>();
        services.AddSingleton<IBlueskySite, BlueskySite>();
        services.AddSingleton<IDeviantArtSite, DeviantArtSite>();
        services.AddSingleton<IE621Site, E621Site>();
        services.AddSingleton<IExHentaiSite, ExHentaiSite>();
        services.AddSingleton<IFurAffinitySite, XFurAffinitySite>();
        services.AddSingleton<IHentaiFoundrySite, HentaiFoundrySite>();
        services.AddSingleton<IInstagramSite, VxInstagramSite>();
        services.AddSingleton<IMisskeySite, MisskeySite>();
        services.AddSingleton<INewgroundsSite, NewgroundsSite>();
        services.AddSingleton<IPixivSite, PixivSite>();
        services.AddSingleton<IRedditSite, RedditSite>();
        services.AddSingleton<ITwitterSite, FxTwitterSite>();
        return services;
    }
}
