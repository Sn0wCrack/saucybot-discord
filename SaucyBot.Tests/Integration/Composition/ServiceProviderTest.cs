using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SaucyBot;
using SaucyBot.Database;
using SaucyBot.Diagnostics;
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
using SaucyBot.Options;
using SaucyBot.Queue;
using SaucyBot.Services;
using SaucyBot.Services.Cache;
using SaucyBot.Site;
using Xunit;

namespace SaucyBot.Tests.Integration.Composition;

public sealed class ServiceProviderTest
{
    [Fact]
    public void ApplicationCompositionBuildsAndResolvesRuntimeServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([
                new KeyValuePair<string, string?>("Sites:FurAffinity:Cookies:A", "a"),
                new KeyValuePair<string, string?>("Sites:FurAffinity:Cookies:B", "b"),
                new KeyValuePair<string, string?>("Sites:ExHentai:Cookies:MemberId", "member"),
                new KeyValuePair<string, string?>("Sites:ExHentai:Cookies:PasswordHash", "password"),
                new KeyValuePair<string, string?>("Sites:HentaiFoundry:SessionCookie", "session"),
                new KeyValuePair<string, string?>("Sites:Pixiv:SessionCookie", "session"),
            ]).Build();

        services.AddSingleton<IConfiguration>(configuration);
        services.Configure<BotOptions>(configuration.GetSection("Bot"));
        services.AddSaucyBotDatabase();
        services.AddSaucyBotCache(configuration);
        services.AddSaucyBotServices();
        services.AddSaucyBotSites();
        services.AddFurAffinityClient(configuration);
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
        services.AddSingleton<IDatabaseMigrator>(Substitute.For<IDatabaseMigrator>());
        services.AddSingleton<IMessageWorkQueue>(Substitute.For<IMessageWorkQueue>());
        services.AddSingleton<IWorkItemProcessor>(Substitute.For<IWorkItemProcessor>());
        services.AddSingleton(new WorkQueueOptions());
        services.AddSingleton<InteractionWorkChannel>();
        services.AddSingleton<WorkQueueHostedService>();
        services.AddSingleton<ISaucyBotMetrics, SaucyBotMetrics>();
        services.AddSingleton<DiscordClientHost>();
        services.AddSingleton<Worker>();
        services.AddHostedService(provider => provider.GetRequiredService<WorkQueueHostedService>());
        services.AddHostedService(provider => provider.GetRequiredService<Worker>());

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        Assert.NotNull(provider.GetRequiredService<Worker>());
        Assert.NotNull(provider.GetRequiredService<WorkQueueHostedService>());
        Assert.NotNull(provider.GetRequiredService<SiteRegistry>());
        Assert.NotNull(provider.GetServices<IHostedService>().Single(hosted => hosted is Worker));

        using var scope = provider.CreateScope();
        Assert.IsType<SaucyBot.Site.Reddit.RedditSite>(scope.ServiceProvider
            .GetRequiredService<SiteRegistry>()
            .Resolve("Reddit", scope.ServiceProvider));
    }
}
