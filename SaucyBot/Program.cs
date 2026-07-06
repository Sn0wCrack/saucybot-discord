using SaucyBot;
using SaucyBot.Database;
using SaucyBot.Library.Sites;
using SaucyBot.Library.Sites.ArtStation;
using SaucyBot.Library.Sites.BlueSky;
using SaucyBot.Library.Sites.DeviantArt;
using SaucyBot.Library.Sites.E621;
using SaucyBot.Library.Sites.ExHentai;
using SaucyBot.Library.Sites.FurAffinity;
using SaucyBot.Library.Sites.HentaiFoundry;
using SaucyBot.Library.Sites.Misskey;
using SaucyBot.Library.Sites.Newgrounds;
using SaucyBot.Library.Sites.Pixiv;
using SaucyBot.Library.Sites.Twitter;
using SaucyBot.Services;
using SaucyBot.Services.Cache;
using SaucyBot.Site;
using Serilog;

await Host.CreateDefaultBuilder(args)
    .UseSerilog((context, configuration) =>
    {
        configuration
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");
    })
    .ConfigureServices((context, services) =>
    {
        var configuration = context.Configuration;

        services.AddSaucyBotDatabase();
        services.AddSaucyBotCache(configuration);
        services.AddSaucyBotServices();
        services.AddSaucyBotSites();

        services.AddFurAffinityClient();
        services.AddArtStationClient();
        services.AddNewgroundsClient();
        services.AddDeviantArtOpenEmbedClient();
        services.AddE621Client();
        services.AddFxTwitterClient();
        services.AddTwitterImageSyndicationClient();
        services.AddMisskeyClient();
        services.AddVixBlueskyClient();
        services.AddPixivClient(configuration);
        services.AddExHentaiClient(configuration);
        services.AddHentaiFoundryClient(configuration);
        services.AddDeviantArtClient();
        services.AddFileDownloadClient();

        services.AddHostedService<Worker>();
    })
    .UseConsoleLifetime()
    .Build()
    .RunAsync();
