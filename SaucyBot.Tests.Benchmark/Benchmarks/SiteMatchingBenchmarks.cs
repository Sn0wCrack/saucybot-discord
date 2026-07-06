using System.Text.RegularExpressions;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
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
using SaucyBot.Site;

namespace SaucyBot.Tests.Benchmark.Benchmarks;

[MemoryDiagnoser]
[MinInvokeCount(3), InvocationCount(16)]      
[MinWarmupCount(3), MaxWarmupCount(5)]
[MinIterationCount(3), MaxIterationCount(5)]
public class SiteMatchingBenchmarks
{
    private FxTwitter _fxTwitter = null!;
    private ArtStation _artStation = null!;
    private Bluesky _bluesky = null!;
    private DeviantArt _deviantArt = null!;
    private E621 _e621 = null!;
    private ExHentai _exHentai = null!;
    private FurAffinity _furAffinity = null!;
    private HentaiFoundry _hentaiFoundry = null!;
    private Instagram _instagram = null!;
    private Misskey _misskey = null!;
    private Newgrounds _newgrounds = null!;
    private Pixiv _pixiv = null!;
    private Reddit _reddit = null!;
    private List<BaseSite> _allSites = null!;

    private const string TweetUrl = "https://twitter.com/username/status/1234567890123456789";
    private const string ArtStationUrl = "https://www.artstation.com/artwork/aBcDeFg";
    private const string BlueskyUrl = "https://bsky.app/profile/user.bsky.social/post/3a7b8c9d1e2f";
    private const string DeviantArtUrl = "https://www.deviantart.com/author/art/My-Art-Title-12345";
    private const string E621Url = "https://e621.net/posts/1234567";
    private const string ExHentaiUrl = "https://exhentai.org/g/1234567/aBcDeFgH01/";
    private const string EHentaiUrl = "https://e-hentai.org/g/1234567/aBcDeFgH01/";
    private const string FurAffinityUrl = "https://www.furaffinity.net/view/12345678/";
    private const string HentaiFoundryUrl = "https://www.hentai-foundry.com/pictures/user/someuser/123456/some-art-slug";
    private const string InstagramUrl = "https://www.instagram.com/p/ABC123def456/";
    private const string MisskeyUrl = "https://misskey.io/notes/abc123def456";
    private const string NewgroundsUrl = "https://www.newgrounds.com/art/view/someuser/some-art-slug";
    private const string PixivUrl = "https://www.pixiv.net/en/artworks/12345678";
    private const string RedditUrl = "https://www.reddit.com/media?url=https%3A%2F%2Fi.redd.it%2Fimage.png";
    private const string NonMatchingText = "Hello world this is just some plain text with no URLs at all";
    private const string MixedText =
        "Check out this tweet https://twitter.com/user/status/123456789 and this art https://www.artstation.com/artwork/aBcDeFg";

    [GlobalSetup]
    public void Setup()
    {
        var config = new ConfigurationBuilder().Build();

        _fxTwitter = CreateFxTwitter(config);
        _artStation = CreateArtStation(config);
        _bluesky = CreateBluesky(config);
        _deviantArt = CreateDeviantArt(config);
        _e621 = CreateE621(config);
        _exHentai = CreateExHentai(config);
        _furAffinity = CreateFurAffinity(config);
        _hentaiFoundry = CreateHentaiFoundry(config);
        _instagram = CreateInstagram(config);
        _misskey = CreateMisskey(config);
        _newgrounds = CreateNewgrounds(config);
        _pixiv = CreatePixiv(config);
        _reddit = CreateReddit(config);

        _allSites =
        [
            _artStation, _bluesky, _deviantArt, _e621, _exHentai,
            _furAffinity, _fxTwitter, _hentaiFoundry, _instagram,
            _misskey, _newgrounds, _pixiv, _reddit
        ];
    }

    [Benchmark]
    public MatchCollection FxTwitter_MatchingTweet() => _fxTwitter.Match(TweetUrl);

    [Benchmark]
    public MatchCollection ArtStation_MatchingArtwork() => _artStation.Match(ArtStationUrl);

    [Benchmark]
    public MatchCollection Bluesky_MatchingPost() => _bluesky.Match(BlueskyUrl);

    [Benchmark]
    public MatchCollection DeviantArt_MatchingArt() => _deviantArt.Match(DeviantArtUrl);

    [Benchmark]
    public MatchCollection E621_MatchingPost() => _e621.Match(E621Url);

    [Benchmark]
    public MatchCollection ExHentai_MatchingGallery() => _exHentai.Match(ExHentaiUrl);

    [Benchmark]
    public MatchCollection EHentai_MatchingGallery() => _exHentai.Match(EHentaiUrl);

    [Benchmark]
    public MatchCollection FurAffinity_MatchingSubmission() => _furAffinity.Match(FurAffinityUrl);

    [Benchmark]
    public MatchCollection HentaiFoundry_MatchingPicture() => _hentaiFoundry.Match(HentaiFoundryUrl);

    [Benchmark]
    public MatchCollection Instagram_MatchingPost() => _instagram.Match(InstagramUrl);

    [Benchmark]
    public MatchCollection Misskey_MatchingNote() => _misskey.Match(MisskeyUrl);

    [Benchmark]
    public MatchCollection Newgrounds_MatchingArt() => _newgrounds.Match(NewgroundsUrl);

    [Benchmark]
    public MatchCollection Pixiv_MatchingArtwork() => _pixiv.Match(PixivUrl);

    [Benchmark]
    public MatchCollection Reddit_MatchingMedia() => _reddit.Match(RedditUrl);

    [Benchmark]
    public int AllSites_NonMatchingText()
    {
        var total = 0;
        foreach (var site in _allSites)
        {
            total += site.Match(NonMatchingText).Count;
        }
        return total;
    }

    [Benchmark]
    public int AllSites_MixedText()
    {
        var total = 0;
        foreach (var site in _allSites)
        {
            total += site.Match(MixedText).Count;
        }
        return total;
    }

    [Benchmark]
    public int AllSites_TweetUrl()
    {
        var total = 0;
        foreach (var site in _allSites)
        {
            total += site.Match(TweetUrl).Count;
        }
        return total;
    }

    private static FxTwitter CreateFxTwitter(IConfiguration config)
    {
        var logger = Substitute.For<ILogger<FxTwitter>>();
        var client = Substitute.For<IFxTwitterClient>();
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(new HttpClient());
        return new FxTwitter(logger, config, client, httpClientFactory);
    }

    private static ArtStation CreateArtStation(IConfiguration config)
    {
        var logger = Substitute.For<ILogger<ArtStation>>();
        var client = Substitute.For<IArtStationClient>();
        return new ArtStation(logger, config, client);
    }

    private static Bluesky CreateBluesky(IConfiguration config)
    {
        var logger = Substitute.For<ILogger<Bluesky>>();
        var client = Substitute.For<IVixBlueskyClient>();
        return new Bluesky(logger, config, client);
    }

    private static DeviantArt CreateDeviantArt(IConfiguration config)
    {
        var logger = Substitute.For<ILogger<DeviantArt>>();
        var client = Substitute.For<IDeviantArtClient>();
        var openEmbedClient = Substitute.For<IDeviantArtOpenEmbedClient>();
        return new DeviantArt(logger, config, client, openEmbedClient);
    }

    private static E621 CreateE621(IConfiguration config)
    {
        var logger = Substitute.For<ILogger<E621>>();
        var client = Substitute.For<IE621Client>();
        return new E621(logger, client);
    }

    private static ExHentai CreateExHentai(IConfiguration config)
    {
        var logger = Substitute.For<ILogger<ExHentai>>();
        var client = Substitute.For<IExHentaiClient>();
        return new ExHentai(logger, config, client);
    }

    private static FurAffinity CreateFurAffinity(IConfiguration config)
    {
        var logger = Substitute.For<ILogger<FurAffinity>>();
        var client = Substitute.For<IFurAffinityClient>();
        return new FurAffinity(logger, client);
    }

    private static HentaiFoundry CreateHentaiFoundry(IConfiguration config)
    {
        var logger = Substitute.For<ILogger<HentaiFoundry>>();
        var client = Substitute.For<IHentaiFoundryClient>();
        return new HentaiFoundry(logger, client);
    }

    private static Instagram CreateInstagram(IConfiguration config)
    {
        var logger = Substitute.For<ILogger<Instagram>>();
        return new Instagram(logger);
    }

    private static Misskey CreateMisskey(IConfiguration config)
    {
        var logger = Substitute.For<ILogger<Misskey>>();
        var client = Substitute.For<IMisskeyClient>();
        return new Misskey(logger, config, client);
    }

    private static Newgrounds CreateNewgrounds(IConfiguration config)
    {
        var logger = Substitute.For<ILogger<Newgrounds>>();
        var client = Substitute.For<INewgroundsClient>();
        return new Newgrounds(logger, client);
    }

    private static Pixiv CreatePixiv(IConfiguration config)
    {
        var logger = Substitute.For<ILogger<Pixiv>>();
        var client = Substitute.For<IPixivClient>();
        var guildConfigManager = Substitute.For<IGuildConfigurationManager>();
        return new Pixiv(logger, config, guildConfigManager, client);
    }

    private static Reddit CreateReddit(IConfiguration config)
    {
        var logger = Substitute.For<ILogger<Reddit>>();
        return new Reddit(logger);
    }
}
