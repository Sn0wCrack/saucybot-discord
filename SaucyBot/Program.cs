using SaucyBot;
using SaucyBot.Library;
using SaucyBot.Library.Sites.ArtStation;
using SaucyBot.Library.Sites.FurAffinity;
using SaucyBot.Library.Sites.Pixiv;
using SaucyBot.Site;
using Serilog;

var host = Host.CreateDefaultBuilder(args)
    .UseSerilog((context, configuration) =>
    {
        configuration
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext()
            .WriteTo.Console();
    })
    .ConfigureServices(services =>
    {
        services.AddSingleton<SiteManager>();
        services.AddSingleton<MessageManager>();
        services.AddSingleton<FaExportClient>();
        services.AddSingleton<PixivClient>();
        services.AddSingleton<ArtStationClient>();

        services.AddSingleton<FurAffinity>();
        services.AddSingleton<Pixiv>();
        services.AddSingleton<ArtStation>();

        services.AddHostedService<Worker>();
    })
    .Build();

await host.RunAsync();