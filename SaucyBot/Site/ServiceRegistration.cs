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
        services.AddSingleton<IArtStationSite, ArtStation.ArtStationSite>();
        services.AddSingleton<IBlueskySite, Bluesky.BlueskySite>();
        services.AddSingleton<IDeviantArtSite, DeviantArt.DeviantArtSite>();
        services.AddSingleton<IE621Site, E621.E621Site>();
        services.AddSingleton<IExHentaiSite, ExHentai.ExHentaiSite>();
        services.AddSingleton<IFurAffinitySite, FurAffinity.XFurAffinitySite>();
        services.AddSingleton<IHentaiFoundrySite, HentaiFoundry.HentaiFoundrySite>();
        services.AddSingleton<IInstagramSite, Instagram.InstagramSite>();
        services.AddSingleton<IMisskeySite, Misskey.MisskeySite>();
        services.AddSingleton<INewgroundsSite, Newgrounds.NewgroundsSite>();
        services.AddSingleton<IPixivSite, Pixiv.PixivSite>();
        services.AddSingleton<IRedditSite, Reddit.RedditSite>();
        services.AddSingleton<ITwitterSite, Twitter.FxTwitterSite>();
        return services;
    }
}
